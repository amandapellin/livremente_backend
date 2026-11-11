using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Dtos;

/// <summary>
/// Item da estante (RF05, #32): o card da publicação (reaproveitado do catálogo)
/// mais o estado de leitura e o progresso. `Status` serializa como string (nome do
/// membro do enum: read, reading, want_to_read, abandoned).
/// <para>
/// `ReadingTimeMinutes` é o tempo de leitura ATIVA acumulado na obra (RF20, soma de
/// `reading_session.active_seconds` em minutos; null se nunca leu) e `Format` é
/// "epub"/"pdf" (derivado da URL do arquivo; null se não há) — ambos alimentam o
/// card da visão Lista da estante (#27). Agregados em lote no `ShelfService`.
/// </para>
/// </summary>
public record ShelfItemDto(
    PublicationCardDto Publication,
    ReadingStatus Status,
    int? ReadPercentage,
    int? LastPageRead,
    int? ReadingTimeMinutes,
    string? Format);
