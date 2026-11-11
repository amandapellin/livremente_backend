namespace LivreMente.Api.Dtos;

// Marcação manual de página (RF21). `bookmarkedPage` é um inteiro >= 0 (0-based,
// como o last_page_read) cuja unidade é definida pelo leitor do front: location
// do epub.js (EPUB) ou página do PDF (input manual; o viewer nativo não expõe a
// página atual). Vale para ambos os formatos (sem restrição).
public record SaveBookmarkRequest(int BookmarkedPage);
