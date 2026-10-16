using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Services;

/// <summary>
/// Regra de negócio do épico de Leitura. Opera sempre sobre a estante do próprio
/// usuário (id vindo do token) — RN04 por construção.
/// </summary>
public class ReadingService(LivreMenteDbContext db, IHttpClientFactory httpClientFactory) : IReadingService
{
    private readonly LivreMenteDbContext _db = db;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

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

        // Upsert na estante: ler já implica "lendo". Se ainda não está na estante,
        // cria com status = reading; caso contrário, só atualiza a última página.
        var shelf = await _db.Shelves
            .FirstOrDefaultAsync(s => s.UserId == userId && s.PublicationId == publicationId, ct);

        if (shelf is null)
        {
            _db.Shelves.Add(new Shelf
            {
                UserId = userId,
                PublicationId = publicationId,
                Status = ReadingStatus.reading,
                LastPageRead = lastPageRead,
            });
        }
        else
        {
            shelf.LastPageRead = lastPageRead;
        }

        await _db.SaveChangesAsync(ct);
        return SaveLastPageResult.Saved;
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

        // Busca no servidor (sem CORS) e devolve os bytes para o front consumir
        // pela mesma origem (proxy do Vite / mesma API em produção). Com retry:
        // Gutenberg/arXiv às vezes derrubam conexões concorrentes (visto como 502).
        var http = _httpClientFactory.CreateClient("publication-files");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                return new PublicationFileResult(GetFileStatus.Ok, bytes, contentType);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt >= 3)
                    return new PublicationFileResult(GetFileStatus.UpstreamError, null, null);
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
            }
        }
    }
}
