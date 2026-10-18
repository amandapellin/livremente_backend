namespace LivreMente.Api.Dtos;

// Grifos + anotações de uma obra para o usuário (RF24): snapshot de "reabrir obra".
// Ambas as listas vêm vazias (não nulas) quando não há nada — nunca erro.
public record ReadingNotesDto(
    IReadOnlyList<HighlightDto> Highlights,
    IReadOnlyList<AnnotationDto> Annotations);
