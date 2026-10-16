namespace LivreMente.Api.Dtos;

/// <summary>
/// Metadados completos de uma publicação (RF13). `Genres` são slugs do front
/// (mesma tradução do card). `Synopsis` vem de `publication.summary`.
/// `DownloadUrl` é o EPUB (ou PDF, se não houver EPUB). Campos de estante/leitura
/// (estado, progresso) pertencem a outros épicos e não entram aqui.
/// </summary>
public record PublicationDetailsDto(
    string Id,
    string Title,
    string Type,
    string Source,
    string? Language,
    int? Year,
    string? Synopsis,
    string? CoverUrl,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres,
    bool PublicDomain,
    string? EpubFileUrl,
    string? PdfFileUrl,
    string? DownloadUrl);
