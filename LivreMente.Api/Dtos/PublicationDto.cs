namespace LivreMente.Api.Dtos;

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
