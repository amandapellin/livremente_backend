namespace LivreMente.Api.Dtos;

// Grifo devolvido ao front (RF22). `id` inteiro (o front adapta); `createdAt` vem
// do banco (create_date). Usado na resposta do POST e na listagem do GET.
public record HighlightDto(int Id, string Excerpt, string EpubPosition, string Color, DateTime CreatedAt);
