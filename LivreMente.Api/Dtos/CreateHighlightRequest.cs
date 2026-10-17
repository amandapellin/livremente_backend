namespace LivreMente.Api.Dtos;

// Criação de grifo (RF22/RN07; só EPUB). `excerpt` é o trecho grifado; `epubPosition`
// é o CFI range do epub.js (<= 255); `color` é um dos slugs do front
// (amarelo|verde|azul|rosa). A nota (annotation) e o capítulo não entram aqui.
public record CreateHighlightRequest(string Excerpt, string EpubPosition, string Color);
