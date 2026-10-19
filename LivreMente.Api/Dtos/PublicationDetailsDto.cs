using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Dtos;

/// <summary>
/// Metadados completos de uma publicação (RF13). `Genres` são slugs do front
/// (mesma tradução do card). `Synopsis` vem de `publication.summary`.
/// `DownloadUrl` é o EPUB (ou PDF, se não houver EPUB).
/// <para>
/// <b>Ciente do usuário (#20, decisão do épico de estante):</b> quando a
/// requisição traz JWT e a obra está na estante do usuário, `ReadingStatus` e
/// `ReadingProgress` vêm preenchidos (habilitam o selo "Na estante · {estado}" e
/// o "Ler agora" que não rebaixa "lido"). Anônimo ou obra fora da estante → ambos
/// nulos. `ReadingStatus` serializa como string (nome do membro do enum).
/// </para>
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
    string? DownloadUrl,
    ReadingStatus? ReadingStatus = null,
    int? ReadingProgress = null,
    int? ReadingTimeMinutes = null);
