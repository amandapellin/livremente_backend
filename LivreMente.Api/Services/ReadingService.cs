using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using LivreMente.Api.Dtos;
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

    public async Task<bool> SaveBookmarkAsync(
        int userId, int publicationId, int bookmarkedPage, CancellationToken ct = default)
    {
        // RF21: a marcação vale para EPUB e PDF — basta a publicação existir (sem
        // gate de formato, diferente do last-page/percentage).
        var exists = await _db.Publications.AsNoTracking().AnyAsync(p => p.Id == publicationId, ct);
        if (!exists)
            return false;

        // Upsert atômico (ON CONFLICT), mesma proteção contra corrida dos auto-saves:
        // marcar implica "lendo", então cria com status = reading; no conflito só
        // atualiza bookmarked_page (não toca em status/last_page_read/read_percentage).
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO shelf (user_id, publication_id, status, bookmarked_page)
            VALUES ({userId}, {publicationId}, 'reading'::reading_status_enum, {bookmarkedPage})
            ON CONFLICT (user_id, publication_id)
            DO UPDATE SET bookmarked_page = EXCLUDED.bookmarked_page", ct);

        return true;
    }

    public async Task<int?> GetBookmarkAsync(int userId, int publicationId, CancellationToken ct = default)
    {
        // Sem linha de estante OU bookmarked_page nulo → null (sem marcação).
        return await _db.Shelves.AsNoTracking()
            .Where(s => s.UserId == userId && s.PublicationId == publicationId)
            .Select(s => s.BookmarkedPage)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<CreateHighlightResult> CreateHighlightAsync(
        int userId, int publicationId, CreateHighlightRequest req, CancellationToken ct = default)
    {
        // RN07: grifo só para EPUB (publicação com epub_file_url).
        var pub = await _db.Publications.AsNoTracking()
            .Where(p => p.Id == publicationId)
            .Select(p => new { p.EpubFileUrl })
            .FirstOrDefaultAsync(ct);

        if (pub is null)
            return new CreateHighlightResult(SaveLastPageResult.PublicationNotFound, null);
        if (string.IsNullOrEmpty(pub.EpubFileUrl))
            return new CreateHighlightResult(SaveLastPageResult.NotEpub, null);

        var highlight = new Highlight
        {
            UserId = userId,
            PublicationId = publicationId,
            Excerpt = req.Excerpt,
            EpubPosition = req.EpubPosition,
            Color = req.Color,
            // CreateDate tem default no banco (CURRENT_TIMESTAMP) e é lido de volta.
        };
        _db.Highlights.Add(highlight);
        await _db.SaveChangesAsync(ct);

        var dto = new HighlightDto(
            highlight.Id, highlight.Excerpt, highlight.EpubPosition, highlight.Color, highlight.CreateDate);
        return new CreateHighlightResult(SaveLastPageResult.Saved, dto);
    }

    public async Task<IReadOnlyList<HighlightDto>> GetHighlightsAsync(
        int userId, int publicationId, CancellationToken ct = default)
    {
        return await _db.Highlights.AsNoTracking()
            .Where(h => h.UserId == userId && h.PublicationId == publicationId)
            .OrderByDescending(h => h.CreateDate).ThenByDescending(h => h.Id)
            .Select(h => new HighlightDto(h.Id, h.Excerpt, h.EpubPosition, h.Color, h.CreateDate))
            .ToListAsync(ct);
    }

    public async Task<CreateAnnotationResult> CreateAnnotationAsync(
        int userId, int publicationId, CreateAnnotationRequest req, CancellationToken ct = default)
    {
        // RN07: anotação só para EPUB (publicação com epub_file_url).
        var pub = await _db.Publications.AsNoTracking()
            .Where(p => p.Id == publicationId)
            .Select(p => new { p.EpubFileUrl })
            .FirstOrDefaultAsync(ct);

        if (pub is null)
            return new CreateAnnotationResult(SaveLastPageResult.PublicationNotFound, null);
        if (string.IsNullOrEmpty(pub.EpubFileUrl))
            return new CreateAnnotationResult(SaveLastPageResult.NotEpub, null);

        var annotation = new Annotation
        {
            UserId = userId,
            PublicationId = publicationId,
            LinkedExcerpt = req.LinkedExcerpt,
            Content = req.Content,
            EpubPosition = req.EpubPosition,
            // CreateDate tem default no banco (CURRENT_TIMESTAMP) e é lido de volta.
        };
        _db.Annotations.Add(annotation);
        await _db.SaveChangesAsync(ct);

        var dto = new AnnotationDto(
            annotation.Id, annotation.LinkedExcerpt, annotation.Content, annotation.EpubPosition, annotation.CreateDate);
        return new CreateAnnotationResult(SaveLastPageResult.Saved, dto);
    }

    public async Task<IReadOnlyList<AnnotationDto>> GetAnnotationsAsync(
        int userId, int publicationId, CancellationToken ct = default)
    {
        return await _db.Annotations.AsNoTracking()
            .Where(a => a.UserId == userId && a.PublicationId == publicationId)
            .OrderByDescending(a => a.CreateDate).ThenByDescending(a => a.Id)
            .Select(a => new AnnotationDto(a.Id, a.LinkedExcerpt, a.Content, a.EpubPosition, a.CreateDate))
            .ToListAsync(ct);
    }

    public async Task<ReadingNotesDto> GetHighlightsAndAnnotationsAsync(
        int userId, int publicationId, CancellationToken ct = default)
    {
        // Sequencial (o DbContext não é thread-safe para consultas concorrentes).
        var highlights = await GetHighlightsAsync(userId, publicationId, ct);
        var annotations = await GetAnnotationsAsync(userId, publicationId, ct);
        return new ReadingNotesDto(highlights, annotations);
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

    public async Task<ReadingCalendarDto> GetCalendarAsync(
        int userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Intervalo [from, to] inclusivo → meia-noite de `from` até meia-noite do dia
        // seguinte a `to` (exclusivo). `start_time` é `timestamp without time zone`
        // (gravado como UTC, Kind=Unspecified), então os limites também são
        // Unspecified — o Npgsql recusaria Kind=Utc nessas colunas.
        var start = from.ToDateTime(TimeOnly.MinValue);
        var endExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        // Agrupa por data do início da sessão; soma o tempo ativo e conta as sessões.
        // Só dias com leitura ATIVA (HAVING soma > 0) — uma sessão aberta sem
        // heartbeat (active_seconds = 0) não conta como frequência.
        var rows = await _db.ReadingSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.StartTime >= start && s.StartTime < endExclusive)
            .GroupBy(s => s.StartTime.Date)
            .Select(g => new
            {
                Date = g.Key,
                ActiveSeconds = g.Sum(s => s.ActiveSeconds),
                SessionCount = g.Count(),
            })
            .Where(x => x.ActiveSeconds > 0)
            .ToListAsync(ct);

        // Minutos com a mesma regra do #20 (mínimo 1 min quando houve leitura).
        var days = rows
            .OrderBy(x => x.Date)
            .Select(x => new ReadingCalendarDayDto(
                DateOnly.FromDateTime(x.Date),
                Math.Max(1, (int)Math.Round(x.ActiveSeconds / 60.0)),
                x.SessionCount))
            .ToList();

        return new ReadingCalendarDto(from, to, days);
    }

    public async Task<ReadingStatsDto> GetStatsAsync(int userId, CancellationToken ct = default)
    {
        // Tempo ativo por dia (UTC) do usuário; só dias com leitura (SUM > 0).
        var rows = await _db.ReadingSessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.StartTime.Date)
            .Select(g => new { Day = g.Key, Seconds = g.Sum(x => (long)x.ActiveSeconds) })
            .Where(x => x.Seconds > 0)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new ReadingStatsDto(0, 0, 0, 0, 0, 0, 0);

        static int ToMinutes(long seconds) => seconds > 0 ? Math.Max(1, (int)Math.Round(seconds / 60.0)) : 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var totalSeconds = rows.Sum(r => r.Seconds);
        var todaySeconds = rows.Where(r => DateOnly.FromDateTime(r.Day) == today).Sum(r => r.Seconds);
        var activeDays = rows.Count;
        var daysThisMonth = rows.Count(r => r.Day.Year == today.Year && r.Day.Month == today.Month);

        // Dias (ordenados) para calcular as sequências.
        var days = rows.Select(r => DateOnly.FromDateTime(r.Day)).OrderBy(d => d).ToList();
        var daySet = days.ToHashSet();

        // Maior sequência: maior corrida de dias consecutivos.
        int longest = 0, run = 0;
        DateOnly? prev = null;
        foreach (var d in days)
        {
            run = prev is DateOnly p && d.DayNumber == p.DayNumber + 1 ? run + 1 : 1;
            if (run > longest) longest = run;
            prev = d;
        }

        // Sequência atual: ancorada em hoje (ou ontem, para não zerar antes de ler hoje).
        var current = 0;
        var last = days[^1];
        if (last == today || last == today.AddDays(-1))
            for (var d = last; daySet.Contains(d); d = d.AddDays(-1))
                current++;

        var totalMinutes = ToMinutes(totalSeconds);
        var dailyAverage = activeDays > 0 ? (int)Math.Round((double)totalMinutes / activeDays) : 0;

        return new ReadingStatsDto(
            totalMinutes, ToMinutes(todaySeconds), dailyAverage,
            current, longest, activeDays, daysThisMonth);
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
