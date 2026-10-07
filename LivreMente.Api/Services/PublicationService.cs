using LivreMente.Api.Dtos;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;
using LivreMente.Api.Mappings;
using Microsoft.EntityFrameworkCore;

namespace LivreMente.Api.Services;

//classe reponsavel pela busca no banco 
public class PublicationService(LivreMenteDbContext db) : IPublicationService
{
    private readonly LivreMenteDbContext _db = db;

    // Um nome da fonte pode corresponder a mais de um slug do front.
    private static readonly ILookup<string, string> BookGenreSlugs =
        PreferenceCatalog.BookGenres
            .SelectMany(entry => entry.Value.Select(name => (Name: name, Slug: entry.Key)))
            .ToLookup(entry => entry.Name, entry => entry.Slug, StringComparer.Ordinal);

    private static readonly ILookup<string, string> ArticleAreaSlugs =
        PreferenceCatalog.ArticleArchives
            .SelectMany(entry => entry.Value.Select(archive => (Archive: archive, Slug: entry.Key)))
            .ToLookup(entry => entry.Archive, entry => entry.Slug, StringComparer.Ordinal);

    public async Task<CatalogPage> ListAsync(
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 50);

        var publications = _db.Publications.AsNoTracking();
        var totals = await publications
            .GroupBy(p => p.Type)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .ToListAsync(ct);

        var books = totals.FirstOrDefault(t => t.Type == PublicationType.book)?.Count ?? 0;
        var articles = totals.FirstOrDefault(t => t.Type == PublicationType.scientific_article)?.Count ?? 0;
        var total = books + articles;
        var counts = new CatalogCountsDto(total, books, articles);
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var offset = ((long)page - 1) * pageSize;

        if (offset >= total)
            return new CatalogPage([], page, pageSize, total, totalPages, counts);

        var rows = await publications
            .OrderBy(p => p.DownloadCount == null)
            .ThenByDescending(p => p.DownloadCount)
            .ThenBy(p => p.Id)
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

        // A tradução pelo catálogo ocorre em memória somente para a página consultada.
        var items = rows.Select(p => new PublicationCardDto(
            p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            p.Title, p.Type.ToString(), p.Source.ToString(), p.Language, p.Year,
            p.Type == PublicationType.scientific_article ? null : p.CoverUrl,
            p.Authors,
            (p.Type == PublicationType.book
                ? p.Genres.SelectMany(name => BookGenreSlugs[name])
                : ArticleAreaSlugs[PreferenceCatalog.ArchiveOf(p.KnowledgeArea ?? "")])
                .Distinct().OrderBy(slug => slug).ToList()))
            .ToList();

        return new CatalogPage(items, page, pageSize, total, totalPages, counts);
    }

    public async Task<PagedResult<PublicationSummaryDto>> SearchAsync(
        string query,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var searchText = query.Trim();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return new PagedResult<PublicationSummaryDto>(
                [], page, pageSize, 0);
        }

        // Normaliza a consulta para evitar que wildcards do SQL alterem a busca
        // e para que entradas como "quixote", "quixote%", "don quixote"
        // e "donquixote" sejam tratadas como a mesma busca por texto.
        var normalizedText = searchText
            .Trim()
            .ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD);

        normalizedText = new string(
            normalizedText
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray())
            .Replace("\\", string.Empty)
            .Replace("%", string.Empty)
            .Replace("_", string.Empty)
            .Replace("$", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Trim();

        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return new PagedResult<PublicationSummaryDto>(
                [], page, pageSize, 0);
        }

        var pattern = $"%{normalizedText}%";

        var publications = _db.Publications
            .Where(p =>
                EF.Functions.ILike(
                    EF.Functions.Unaccent(p.Title),
                    EF.Functions.Unaccent(pattern),
                    "\\")
                ||
                p.Authors.Any(a =>
                    EF.Functions.ILike(
                        EF.Functions.Unaccent(a.Name),
                        EF.Functions.Unaccent(pattern),
                        "\\")));
        
        var total = await publications.CountAsync(ct);

        long offset = (long)(page - 1) * pageSize;

        if (offset >= total)
        {
            return new PagedResult<PublicationSummaryDto>(
                [], page, pageSize, total);
        }

        var items = await publications
            .OrderByDescending(p => p.DownloadCount)
            .ThenBy(p => p.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(p => new PublicationSummaryDto(
                p.Id,
                p.Title,
                p.Source.ToString(),
                p.Type.ToString(),
                p.Language,
                p.Year,
                p.CoverUrl,
                p.Authors.Select(a => a.Name).ToList(),
                p.Genres.Select(g => g.Name).ToList()))
            .ToListAsync(ct);

        return new PagedResult<PublicationSummaryDto>(
            items, page, pageSize, total);
    }
}
