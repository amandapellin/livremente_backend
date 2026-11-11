namespace LivreMente.Api.Dtos;

public record PublicationCardDto(
    string Id,
    string Title,
    string Type,
    string Source,
    string? Language,
    int? Year,
    string? CoverUrl,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres);
