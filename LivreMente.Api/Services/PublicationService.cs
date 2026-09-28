using LivreMente.Api.Dtos;
using LivreMente.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LivreMente.Api.Services;

//classe reponsavel pela busca no banco 
public class PublicationService(LivreMenteDbContext db) : IPublicationService
{
    private readonly LivreMenteDbContext _db = db;

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