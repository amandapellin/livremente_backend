using LivreMente.Api.Dtos;

namespace LivreMente.Api.Services;

public interface IGenreService
{
    Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct = default);

    Task<GenreDto?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<PagedResult<PublicationSummaryDto>> GetPublicationsAsync(
        int genreId, int page = 1, int pageSize = 20, CancellationToken ct = default);
}
