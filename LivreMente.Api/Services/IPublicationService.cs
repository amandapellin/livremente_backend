using LivreMente.Api.Dtos;
using LivreMente.Api.Validation;

namespace LivreMente.Api.Services;

public interface IPublicationService
{
    Task<CatalogPage> ListAsync(
        string? q = null,
        CatalogFilters? filters = null,
        int page = 1,
        int pageSize = 10,
        string? sort = null,
        CancellationToken ct = default);

    /// <summary>Metadados completos de uma publicação (RF13). Null se não existir.</summary>
    Task<PublicationDetailsDto?> GetByIdAsync(int id, CancellationToken ct = default);
}
