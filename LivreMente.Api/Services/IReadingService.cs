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

// Status reusa SaveLastPageResult (mesma família EPUB-only: Saved/PublicationNotFound/
// NotEpub). Highlight != null só quando Saved.
public record CreateHighlightResult(SaveLastPageResult Status, Dtos.HighlightDto? Highlight);

// Idem para anotação (RF23). Annotation != null só quando Saved.
public record CreateAnnotationResult(SaveLastPageResult Status, Dtos.AnnotationDto? Annotation);

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
    /// Atualiza o percentual lido (RF19) na estante do usuário. Só EPUB; cria a
    /// linha de estante (status = reading) se não existir. O 0–100 é validado no endpoint.
    /// </summary>
    Task<SaveLastPageResult> SavePercentageAsync(int userId, int publicationId, int readPercentage, CancellationToken ct = default);

    /// <summary>
    /// Abre uma sessão de leitura (RF20) em `reading_session` (start_time = agora).
    /// Vale para EPUB e PDF. Retorna o id da sessão, ou null se a publicação não existe.
    /// </summary>
    Task<int?> StartSessionAsync(int userId, int publicationId, CancellationToken ct = default);

    /// <summary>
    /// Atualiza o tempo ativo da sessão (RF20): grava `active_seconds` e move
    /// `end_time` para agora (heartbeat/encerramento). Só a sessão do próprio
    /// usuário. Retorna false se a sessão não existe (ou não é dele).
    /// </summary>
    Task<bool> UpdateSessionAsync(int userId, int sessionId, int activeSeconds, CancellationToken ct = default);

    /// <summary>
    /// Marcação manual de página (RF21): grava `bookmarked_page` na estante do
    /// usuário. Vale para EPUB e PDF (sem restrição de formato); cria a linha de
    /// estante (status = reading) se não existir. Retorna false se a publicação
    /// não existe. O >= 0 (0-based, como o last_page_read) é validado no endpoint.
    /// </summary>
    Task<bool> SaveBookmarkAsync(int userId, int publicationId, int bookmarkedPage, CancellationToken ct = default);

    /// <summary>
    /// Leitura da marcação manual (RF21): a página marcada do par usuário/publicação,
    /// ou null se não há marcação (sem linha de estante ou `bookmarked_page` nulo).
    /// </summary>
    Task<int?> GetBookmarkAsync(int userId, int publicationId, CancellationToken ct = default);

    /// <summary>
    /// Cria um grifo (RF22/RN07) em `highlight`. **Só EPUB** (publicação com
    /// `epub_file_url`); não-EPUB → NotEpub; inexistente → PublicationNotFound.
    /// Devolve o grifo criado (com id e create_date do banco) quando Saved.
    /// </summary>
    Task<CreateHighlightResult> CreateHighlightAsync(int userId, int publicationId, Dtos.CreateHighlightRequest req, CancellationToken ct = default);

    /// <summary>
    /// Lista os grifos do par usuário/publicação (RF22), mais recentes primeiro.
    /// </summary>
    Task<IReadOnlyList<Dtos.HighlightDto>> GetHighlightsAsync(int userId, int publicationId, CancellationToken ct = default);

    /// <summary>
    /// Cria uma anotação (RF23/RN07) em `annotation`. **Só EPUB** (publicação com
    /// `epub_file_url`); não-EPUB → NotEpub; inexistente → PublicationNotFound.
    /// Devolve a anotação criada (com id e create_date do banco) quando Saved.
    /// </summary>
    Task<CreateAnnotationResult> CreateAnnotationAsync(int userId, int publicationId, Dtos.CreateAnnotationRequest req, CancellationToken ct = default);

    /// <summary>
    /// Lista as anotações do par usuário/publicação (RF23), mais recentes primeiro.
    /// </summary>
    Task<IReadOnlyList<Dtos.AnnotationDto>> GetAnnotationsAsync(int userId, int publicationId, CancellationToken ct = default);

    /// <summary>
    /// Baixa, no servidor, o arquivo (EPUB/PDF) da fonte externa e o devolve em
    /// bytes, para o front consumi-lo pela mesma origem (sem CORS). RF15/RF16.
    /// </summary>
    Task<PublicationFileResult> GetFileAsync(int publicationId, PublicationFileKind kind, CancellationToken ct = default);
}
