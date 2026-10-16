namespace LivreMente.Api.Dtos;

// Salvamento automático da última página lida (RF17/RN08). `lastPageRead` é a
// posição/página inteira do leitor EPUB.
public record SaveLastPageRequest(int LastPageRead);
