namespace LivreMente.Api.Dtos;

// Marcação manual de página (RF21). `bookmarkedPage` é um inteiro > 0 (1-based,
// conforme o CHECK shelf_bookmarked_page_check do banco) cuja unidade é definida
// pelo leitor do front: location do epub.js (EPUB) ou página do PDF. Vale para
// ambos os formatos (sem restrição).
public record SaveBookmarkRequest(int BookmarkedPage);
