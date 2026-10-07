using LivreMente.Api.Dtos;

namespace LivreMente.Api.Services;

public interface IPublicationService
{
    Task<CatalogPage> ListAsync(
        string? q = null,
        int page = 1,
        int pageSize = 10,
        string? sort = null,
        CancellationToken ct = default);

    Task<PagedResult<PublicationSummaryDto>> SearchAsync(
        string query,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

}
