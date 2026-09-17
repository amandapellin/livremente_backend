using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Dtos;
using LivreMente.Api.Models;

namespace LivreMente.Api.Services;

/// <summary>
/// Regras de negócio de gêneros. Toda a lógica de acesso a dados e de
/// transformação entidade → DTO fica aqui; os endpoints apenas delegam.
/// </summary>
public class GenreService(LivreMenteDbContext db) : IGenreService
{
    private readonly LivreMenteDbContext _db = db;

    public async Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Genres
            .OrderBy(g => g.Name)
            .Select(g => new GenreDto(g.Id, g.Name, g.Publications.Count))
            .ToListAsync(ct);
    }

    public async Task<GenreDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Genres
            .Where(g => g.Id == id)
            .Select(g => new GenreDto(g.Id, g.Name, g.Publications.Count))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PagedResult<PublicationSummaryDto>> GetPublicationsAsync(
        int genreId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var query = _db.Publications
            .Where(p => p.Genres.Any(g => g.Id == genreId))
            .OrderByDescending(p => p.DownloadCount);

        var total = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
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

        return new PagedResult<PublicationSummaryDto>(items, page, pageSize, total);
    }
}
