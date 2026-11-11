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

    /// <summary>
    /// Metadados completos de uma publicação (RF13). Null se não existir.
    /// Com <paramref name="userId"/> (JWT presente, #20), popula o estado/progresso
    /// da estante do usuário para a obra; sem ele (anônimo), vêm nulos.
    /// </summary>
    Task<PublicationDetailsDto?> GetByIdAsync(int id, int? userId = null, CancellationToken ct = default);
}
