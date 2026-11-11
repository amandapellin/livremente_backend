namespace LivreMente.Api.Dtos;

// Atualização PARCIAL (merge): campo null/ausente = não tocado. Permite que o
// perfil (chips EAV) e a seção "Leitor e interface" (tema/toggles) gravem
// independentemente no mesmo endpoint.
public record UpdatePreferencesRequest (
    IReadOnlyList<string>? Languages,
    IReadOnlyList<string>? ContentTypes,
    IReadOnlyList<string>? KnowledgeAreas,
    string? Theme,
    bool? ResumeAuto,
    bool? SaveDictionary
);
