using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Dtos;

/// <summary>
/// Item da estante (RF05, #32): o card da publicação (reaproveitado do catálogo)
/// mais o estado de leitura e o progresso. `Status` serializa como string (nome do
/// membro do enum: read, reading, want_to_read, abandoned).
/// </summary>
public record ShelfItemDto(
    PublicationCardDto Publication,
    ReadingStatus Status,
    int? ReadPercentage,
    int? LastPageRead);
