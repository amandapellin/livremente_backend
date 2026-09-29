namespace LivreMente.Api.Dtos;

public record UserPreferencesDto(
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> ContentTypes,
    IReadOnlyList<string> KnowledgeAreas
);