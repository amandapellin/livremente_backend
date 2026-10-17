namespace LivreMente.Api.Dtos;

// Leitura da marcação manual (RF21): a página marcada, ou null se o usuário não
// marcou nenhuma para essa publicação.
public record BookmarkDto(int? BookmarkedPage);
