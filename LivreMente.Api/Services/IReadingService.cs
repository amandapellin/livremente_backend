namespace LivreMente.Api.Services;

public enum SaveLastPageResult
{
    Saved,
    PublicationNotFound,
    NotEpub,
}

public enum PublicationFileKind
{
    Epub,
    Pdf,
}

public enum GetFileStatus
{
    Ok,
    PublicationNotFound,
    NotAvailable,   // a publicação não tem arquivo desse tipo
    UpstreamError,  // falha ao buscar na fonte externa (Gutendex/arXiv)
}

public record PublicationFileResult(GetFileStatus Status, byte[]? Content, string? ContentType);

public interface IReadingService
{
    /// <summary>
    /// Salva a última página lida (RF17/RN08) na estante do usuário. Só aceita
    /// publicações EPUB; cria a linha de estante (status = reading) se não existir.
    /// </summary>
    Task<SaveLastPageResult> SaveLastPageAsync(int userId, int publicationId, int lastPageRead, CancellationToken ct = default);

    /// <summary>
    /// Retomada de leitura (RF18): última página lida do par usuário/publicação,
    /// ou null se o usuário nunca leu essa publicação.
    /// </summary>
    Task<int?> GetLastPageAsync(int userId, int publicationId, CancellationToken ct = default);

    /// <summary>
    /// Baixa, no servidor, o arquivo (EPUB/PDF) da fonte externa e o devolve em
    /// bytes, para o front consumi-lo pela mesma origem (sem CORS). RF15/RF16.
    /// </summary>
    Task<PublicationFileResult> GetFileAsync(int publicationId, PublicationFileKind kind, CancellationToken ct = default);
}
