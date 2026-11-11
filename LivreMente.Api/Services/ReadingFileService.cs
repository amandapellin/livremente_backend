using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using LivreMente.Api.Models;

namespace LivreMente.Api.Services;

/// <summary>
/// Proxy same-origin dos arquivos de leitura (EPUB/PDF) e das capas: baixa no
/// servidor (sem CORS) via <see cref="IHttpClientFactory"/>, com cache em memória,
/// single-flight por arquivo e retry em falha transitória. Extraído do
/// <see cref="ReadingService"/> (parte autocontida, baixo acoplamento com o resto
/// do épico de Leitura).
/// </summary>
public class ReadingFileService(
    LivreMenteDbContext db, IHttpClientFactory httpClientFactory, IMemoryCache cache,
    IConfiguration configuration) : IReadingFileService
{
    private readonly LivreMenteDbContext _db = db;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IMemoryCache _cache = cache;
    private readonly IConfiguration _configuration = configuration;

    // Single-flight por arquivo: garante UMA busca no upstream mesmo com requisições
    // concorrentes (StrictMode do dev dispara em dobro; evita 502 por concorrência).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileGates = new();

    public async Task<PublicationFileResult> GetFileAsync(
        int publicationId, PublicationFileKind kind, CancellationToken ct = default)
    {
        var pub = await _db.Publications.AsNoTracking()
            .Where(p => p.Id == publicationId)
            .Select(p => new { p.EpubFileUrl, p.PdfFileUrl })
            .FirstOrDefaultAsync(ct);

        if (pub is null)
            return new PublicationFileResult(GetFileStatus.PublicationNotFound, null, null);

        var (url, contentType) = kind == PublicationFileKind.Epub
            ? (pub.EpubFileUrl, "application/epub+zip")
            : (pub.PdfFileUrl, "application/pdf");

        if (string.IsNullOrEmpty(url))
            return new PublicationFileResult(GetFileStatus.NotAvailable, null, null);

        var cacheKey = $"pubfile:{publicationId}:{kind}";
        if (_cache.TryGetValue(cacheKey, out byte[]? cached) && cached is not null)
            return new PublicationFileResult(GetFileStatus.Ok, cached, contentType);

        // Single-flight: concorrentes aguardam a mesma busca e reusam o resultado.
        var gate = FileGates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
                return new PublicationFileResult(GetFileStatus.Ok, cached, contentType);

            // EPUB prefere a versão SEM imagens do Gutenberg (muito menor: ex. PnP
            // 24,8 MB → 561 KB), com fallback à original. PDF usa só a URL original.
            var candidates = kind == PublicationFileKind.Epub && PreferNoImagesEpub()
                ? NoImagesFirst(url)
                : [url];

            var bytes = await DownloadFirstAsync(candidates, ct);
            if (bytes is null)
                return new PublicationFileResult(GetFileStatus.UpstreamError, null, null);

            // Cacheia o arquivo (reaberturas e o fetch duplicado do StrictMode ficam instantâneos).
            _cache.Set(cacheKey, bytes, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30),
                Size = bytes.Length,
            });
            return new PublicationFileResult(GetFileStatus.Ok, bytes, contentType);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PublicationFileResult> GetCoverAsync(int publicationId, CancellationToken ct = default)
    {
        // Projeta num tipo anônimo para distinguir "obra não existe" de "obra sem capa"
        // (um Select direto de string? devolveria null nos dois casos).
        var row = await _db.Publications.AsNoTracking()
            .Where(p => p.Id == publicationId)
            .Select(p => new { p.CoverUrl })
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return new PublicationFileResult(GetFileStatus.PublicationNotFound, null, null);
        if (string.IsNullOrEmpty(row.CoverUrl))
            return new PublicationFileResult(GetFileStatus.NotAvailable, null, null);

        // As capas do Gutendex são JPEG (pg*.cover.medium.jpg).
        const string contentType = "image/jpeg";
        var cacheKey = $"pubcover:{publicationId}";
        if (_cache.TryGetValue(cacheKey, out byte[]? cached) && cached is not null)
            return new PublicationFileResult(GetFileStatus.Ok, cached, contentType);

        // Single-flight + cache, mesmo padrão do proxy de EPUB/PDF.
        var gate = FileGates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
                return new PublicationFileResult(GetFileStatus.Ok, cached, contentType);

            var bytes = await DownloadWithRetryAsync(row.CoverUrl, ct);
            if (bytes is null)
                return new PublicationFileResult(GetFileStatus.UpstreamError, null, null);

            _cache.Set(cacheKey, bytes, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30),
                Size = bytes.Length,
            });
            return new PublicationFileResult(GetFileStatus.Ok, bytes, contentType);
        }
        finally
        {
            gate.Release();
        }
    }

    private bool PreferNoImagesEpub() =>
        _configuration.GetValue<bool?>("Reading:PreferNoImagesEpub") ?? true;

    // Candidatos para EPUB: a versão sem imagens primeiro, a original como fallback.
    private static string[] NoImagesFirst(string url)
    {
        var noImages = url
            .Replace(".epub3.images", ".epub.noimages", StringComparison.Ordinal)
            .Replace(".epub.images", ".epub.noimages", StringComparison.Ordinal);
        return noImages == url ? [url] : [noImages, url];
    }

    // Tenta os candidatos em ordem; devolve o primeiro que baixar, ou null.
    private async Task<byte[]?> DownloadFirstAsync(IReadOnlyList<string> urls, CancellationToken ct)
    {
        foreach (var url in urls)
        {
            var bytes = await DownloadWithRetryAsync(url, ct);
            if (bytes is not null)
                return bytes;
        }
        return null;
    }

    // Busca no servidor (sem CORS). 404 → null (não existe; tenta o próximo candidato).
    // Retry só em falha transitória (rede/5xx — Gutenberg derruba conexões concorrentes).
    private async Task<byte[]?> DownloadWithRetryAsync(string url, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("publication-files");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt >= 3)
                    return null;
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
            }
        }
    }
}
