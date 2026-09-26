using LivreMente.Api.Dtos;

namespace LivreMente.Api.Services;

public interface IPublicationService
{
    Task<PagedResult<PublicationSummaryDto>> SearchAsync(
        string query,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

}