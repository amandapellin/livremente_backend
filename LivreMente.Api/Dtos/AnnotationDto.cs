namespace LivreMente.Api.Dtos;

// Anotação devolvida ao front (RF23). `id` inteiro; `createdAt` vem do banco
// (create_date). Usado na resposta do POST e na listagem do GET.
public record AnnotationDto(int Id, string LinkedExcerpt, string Content, string EpubPosition, DateTime CreatedAt);
