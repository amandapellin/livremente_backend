namespace LivreMente.Api.Dtos;

/// <summary>Resumo de uma publicação para listagens (não expõe a entidade EF crua).</summary>
public record PublicationSummaryDto(
    int Id,
    string Title,
    string Source,
    string Type,
    string? Language,
    int? Year,
    string? CoverUrl,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres);
