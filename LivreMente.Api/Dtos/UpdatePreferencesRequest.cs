namespace LivreMente.Api.Dtos;
public record UpdatePreferencesRequest (
    IReadOnlyList<string>? Languages,
    IReadOnlyList<string>? ContentTypes,
    IReadOnlyList<string>? KnowledgeAreas
);
