namespace LivreMente.Api.Dtos;

// Criação de anotação (RF23/RN07; só EPUB). `linkedExcerpt` é o trecho vinculado;
// `content` é a nota do usuário; `epubPosition` é o CFI range do epub.js (<= 255).
public record CreateAnnotationRequest(string LinkedExcerpt, string Content, string EpubPosition);
