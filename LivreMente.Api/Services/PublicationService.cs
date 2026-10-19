using LivreMente.Api.Dtos;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;
using LivreMente.Api.Mappings;
using LivreMente.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace LivreMente.Api.Services;

public class PublicationService(LivreMenteDbContext db) : IPublicationService
{
    private readonly LivreMenteDbContext _db = db;

    public async Task<CatalogPage> ListAsync(
        string? q = null,
        CatalogFilters? filters = null,
        int page = 1,
        int pageSize = 10,
        string? sort = null,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 50);

        // Base = busca (q) + filtros EXCETO type. Os counts saem daqui (RF12: os
        // chips de tipo mostram quantos há de cada sob os demais filtros ativos).
        var baseQuery = _db.Publications.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            baseQuery = ApplySearch(baseQuery, q);
        if (filters is not null)
            baseQuery = ApplyFilters(baseQuery, filters);

        var totals = await baseQuery
            .GroupBy(p => p.Type)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .ToListAsync(ct);

        var books = totals.FirstOrDefault(t => t.Type == PublicationType.book)?.Count ?? 0;
        var articles = totals.FirstOrDefault(t => t.Type == PublicationType.scientific_article)?.Count ?? 0;
        var counts = new CatalogCountsDto(books + articles, books, articles);

        // A lista paginada aplica o type por cima; o total reflete o que é listado
        // (quando há type, é a contagem daquele tipo já calculada nos counts).
        var publications = baseQuery;
        var total = books + articles;
        if (filters?.Type is PublicationType type)
        {
            publications = baseQuery.Where(p => p.Type == type);
            total = type == PublicationType.book ? books : articles;
        }

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var offset = ((long)page - 1) * pageSize;

        if (offset >= total)
            return new CatalogPage([], page, pageSize, total, totalPages, counts);

        // Relevância usa popularidade como ordenação inicial, inclusive para valores desconhecidos.
        var ordered = sort?.Trim().ToLowerInvariant() switch
        {
            "recent" => publications.OrderBy(p => p.Year == null).ThenByDescending(p => p.Year),
            "title" => publications.OrderBy(p => p.Title),
            _ => publications.OrderBy(p => p.DownloadCount == null).ThenByDescending(p => p.DownloadCount),
        };

        var rows = await ordered.ThenBy(p => p.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id, p.Title, p.Type, p.Source, p.Language, p.Year,
                p.CoverUrl, p.KnowledgeArea,
                Authors = p.Authors.OrderBy(a => a.Name).Select(a => a.Name).ToList(),
                Genres = p.Genres.Select(g => g.Name).ToList(),
            })
            .ToListAsync(ct);

        // A tradução pelo catálogo ocorre em memória somente para a página consultada
        // (mapper compartilhado com a estante — #32).
        var items = rows.Select(p => new PublicationCardDto(
            p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            p.Title, p.Type.ToString(), p.Source.ToString(), p.Language, p.Year,
            PublicationCardMapper.CoverFor(p.Type, p.CoverUrl),
            p.Authors,
            PublicationCardMapper.GenreSlugs(p.Type, p.Genres, p.KnowledgeArea)))
            .ToList();

        return new CatalogPage(items, page, pageSize, total, totalPages, counts);
    }

    // RF12 — filtros combináveis (AND entre filtros distintos; OR dentro de cada lista).
    // O type NÃO entra aqui: é aplicado só na listagem, para os counts refletirem os
    // demais filtros sem o type (ver ListAsync).
    private static IQueryable<Publication> ApplyFilters(IQueryable<Publication> publications, CatalogFilters filters)
    {
        if (filters.Languages.Length > 0)
            publications = publications.Where(p =>
                p.Language != null && filters.Languages.Contains(p.Language));

        if (filters.HasGenreFilter)
        {
            // Livro casa pela M:N genre.name; artigo casa pela área (KnowledgeArea).
            // Slugs já traduzidos na validação. Se a seleção for só de livro, a lista
            // de archives fica vazia e nenhum artigo casa (e vice-versa) — faceta correta.
            var bookNames = filters.GenreBookNames;
            var archives = filters.GenreArchives;
            publications = publications.Where(p =>
                (p.Type == PublicationType.book && p.Genres.Any(g => bookNames.Contains(g.Name)))
                || (p.Type == PublicationType.scientific_article
                    && p.KnowledgeArea != null && archives.Contains(p.KnowledgeArea)));
        }

        return publications;
    }

    public async Task<PublicationDetailsDto?> GetByIdAsync(int id, int? userId = null, CancellationToken ct = default)
    {
        var p = await _db.Publications.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.Title, x.Type, x.Source, x.Language, x.Year, x.Summary,
                x.CoverUrl, x.KnowledgeArea, x.EpubFileUrl, x.PdfFileUrl, x.CopyrightFlag,
                Authors = x.Authors.OrderBy(a => a.Name).Select(a => a.Name).ToList(),
                Genres = x.Genres.Select(g => g.Name).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (p is null)
            return null;

        // Mesma tradução nome→slug do card (mapper compartilhado).
        var genreSlugs = PublicationCardMapper.GenreSlugs(p.Type, p.Genres, p.KnowledgeArea);

        // #20 ciente do usuário: se há JWT (userId) e a obra está na estante, traz o
        // estado + progresso (habilita o selo "Na estante · {estado}" e o "Ler agora"
        // que não rebaixa "lido"). Anônimo ou fora da estante → nulos.
        ReadingStatus? readingStatus = null;
        int? readingProgress = null;
        int? readingTimeMinutes = null;
        if (userId is int uid)
        {
            var onShelf = await _db.Shelves.AsNoTracking()
                .Where(s => s.UserId == uid && s.PublicationId == id)
                .Select(s => new { s.Status, s.ReadPercentage })
                .FirstOrDefaultAsync(ct);
            if (onShelf is not null)
            {
                readingStatus = onShelf.Status;
                readingProgress = onShelf.ReadPercentage;
            }

            // Tempo total de leitura ATIVA (RF20): soma o active_seconds das sessões
            // da obra. Em minutos (mínimo 1 min se houve leitura; null se nenhuma).
            var totalSeconds = await _db.ReadingSessions.AsNoTracking()
                .Where(s => s.UserId == uid && s.PublicationId == id)
                .SumAsync(s => (int?)s.ActiveSeconds, ct) ?? 0;
            readingTimeMinutes = totalSeconds > 0 ? Math.Max(1, (int)Math.Round(totalSeconds / 60.0)) : null;
        }

        return new PublicationDetailsDto(
            p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            p.Title, p.Type.ToString(), p.Source.ToString(), p.Language, p.Year,
            p.Summary,
            PublicationCardMapper.CoverFor(p.Type, p.CoverUrl),
            p.Authors, genreSlugs,
            !p.CopyrightFlag,
            p.EpubFileUrl, p.PdfFileUrl,
            p.EpubFileUrl ?? p.PdfFileUrl,
            readingStatus, readingProgress, readingTimeMinutes);
    }

    private static IQueryable<Publication> ApplySearch(IQueryable<Publication> publications, string text)
    {
        // Preserva espaços/hífens e trata metacaracteres de LIKE como texto literal.
        var escaped = text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var pattern = $"%{escaped}%";
        return publications.Where(p =>
            EF.Functions.ILike(EF.Functions.Unaccent(p.Title), EF.Functions.Unaccent(pattern), "\\")
            || p.Authors.Any(a =>
                EF.Functions.ILike(EF.Functions.Unaccent(a.Name), EF.Functions.Unaccent(pattern), "\\")));
    }
}
