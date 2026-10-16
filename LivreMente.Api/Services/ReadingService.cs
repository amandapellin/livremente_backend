using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Services;

/// <summary>
/// Regra de negócio do épico de Leitura. Opera sempre sobre a estante do próprio
/// usuário (id vindo do token) — RN04 por construção.
/// </summary>
public class ReadingService(
    LivreMenteDbContext db, IHttpClientFactory httpClientFactory, IMemoryCache cache,
    IConfiguration configuration) : IReadingService
{
    private readonly LivreMenteDbContext _db = db;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IMemoryCache _cache = cache;
    private readonly IConfiguration _configuration = configuration;

    // Single-flight por arquivo: garante UMA busca no upstream mesmo com requisições
    // concorrentes (StrictMode do dev dispara em dobro; evita 502 por concorrência).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileGates = new();

    /// <summary>UTC como Unspecified — exigido pelas colunas `timestamp without time zone`.</summary>
    private static DateTime Now() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    public async Task<SaveLastPageResult> SaveLastPageAsync(
        int userId, int publicationId, int lastPageRead, CancellationToken ct = default)
    {
        // RF17/RN08: só EPUB tem "última página". EPUB = possui arquivo EPUB
        // (livros do Gutendex); artigos (PDF) não.
        var pub = await _db.Publications.AsNoTracking()
            .Where(p => p.Id == publicationId)
            .Select(p => new { p.EpubFileUrl })
            .FirstOrDefaultAsync(ct);

        if (pub is null)
            return SaveLastPageResult.PublicationNotFound;
        if (string.IsNullOrEmpty(pub.EpubFileUrl))
            return SaveLastPageResult.NotEpub;

        // Upsert ATÔMICO na estante (ON CONFLICT): ler já implica "lendo", então
        // cria com status = reading se não existir; senão só atualiza a última
        // página. Atômico para não quebrar com auto-saves concorrentes (o
        // find-or-create inseria em corrida e violava uq_shelf_user_publication).
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO shelf (user_id, publication_id, status, last_page_read)
            VALUES ({userId}, {publicationId}, 'reading'::reading_status_enum, {lastPageRead})
            ON CONFLICT (user_id, publication_id)
            DO UPDATE SET last_page_read = EXCLUDED.last_page_read", ct);

        return SaveLastPageResult.Saved;
    }

    public async Task<SaveLastPageResult> SavePercentageAsync(
        int userId, int publicationId, int readPercentage, CancellationToken ct = default)
    {
        // RF19: percentual só faz sentido para EPUB (livros do Gutendex).
        var pub = await _db.Publications.AsNoTracking()
            .Where(p => p.Id == publicationId)
            .Select(p => new { p.EpubFileUrl })
            .FirstOrDefaultAsync(ct);

        if (pub is null)
            return SaveLastPageResult.PublicationNotFound;
        if (string.IsNullOrEmpty(pub.EpubFileUrl))
            return SaveLastPageResult.NotEpub;

        // Upsert atômico (mesma proteção contra corrida do auto-save do #22).
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO shelf (user_id, publication_id, status, read_percentage)
            VALUES ({userId}, {publicationId}, 'reading'::reading_status_enum, {readPercentage})
            ON CONFLICT (user_id, publication_id)
            DO UPDATE SET read_percentage = EXCLUDED.read_percentage", ct);

        return SaveLastPageResult.Saved;
    }

    public async Task<int?> StartSessionAsync(int userId, int publicationId, CancellationToken ct = default)
    {
        // RF20: vale para EPUB e PDF — basta a publicação existir.
        var exists = await _db.Publications.AsNoTracking().AnyAsync(p => p.Id == publicationId, ct);
        if (!exists)
            return null;

        var session = new ReadingSession
        {
            UserId = userId,
            PublicationId = publicationId,
            StartTime = Now(),
            ActiveSeconds = 0,
        };
        _db.ReadingSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session.Id;
    }

    public async Task<bool> UpdateSessionAsync(int userId, int sessionId, int activeSeconds, CancellationToken ct = default)
    {
        // Só a sessão do próprio usuário (sem revelar as de outros) — RN04.
        var session = await _db.ReadingSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null)
            return false;

        session.ActiveSeconds = activeSeconds;
        session.EndTime = Now(); // end_time acompanha o último heartbeat
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int?> GetLastPageAsync(int userId, int publicationId, CancellationToken ct = default)
    {
        // Sem linha de estante OU last_page_read nulo → null (nunca leu / não salvo).
        return await _db.Shelves.AsNoTracking()
            .Where(s => s.UserId == userId && s.PublicationId == publicationId)
            .Select(s => s.LastPageRead)
            .FirstOrDefaultAsync(ct);
    }

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
