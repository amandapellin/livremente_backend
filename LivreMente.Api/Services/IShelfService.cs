using LivreMente.Api.Dtos;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Services;

public interface IShelfService
{
    /// <summary>
    /// RF05 (#32): lista a estante do usuário, opcionalmente filtrada por estado.
    /// Cada item traz o card da publicação + estado + progresso. RN04 (só o usuário).
    /// </summary>
    Task<IReadOnlyList<ShelfItemDto>> GetShelfAsync(int userId, ReadingStatus? status, CancellationToken ct = default);

    /// <summary>
    /// RF06 (#33): upsert do estado da obra na estante (cria se não existe; senão
    /// atualiza só o `status`, preservando o progresso). RN05 por construção.
    /// `true` = aplicado; `false` = publicação inexistente (→ 404).
    /// </summary>
    Task<bool> SetStatusAsync(int userId, int publicationId, ReadingStatus status, CancellationToken ct = default);

    /// <summary>
    /// RF06 (#33): remove a obra da estante. `true` = removida; `false` = não estava
    /// na estante (→ 404).
    /// </summary>
    Task<bool> RemoveAsync(int userId, int publicationId, CancellationToken ct = default);
}
