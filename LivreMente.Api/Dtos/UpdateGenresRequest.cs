namespace LivreMente.Api.Dtos;

public record UpdateGenresRequest(
    IReadOnlyList<string>? Genres
);
