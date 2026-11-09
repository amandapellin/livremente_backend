namespace LivreMente.Api.Services;

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

/// <summary>
/// Proxy same-origin dos arquivos de leitura (EPUB/PDF — RF15/RF16) e das capas
/// (#27): baixa no servidor (sem CORS), com cache em memória + single-flight por
/// arquivo e retry em falha transitória. Extraído do <see cref="IReadingService"/>
/// para mantê-lo focado em progresso/notas/sessão/stats.
/// </summary>
public interface IReadingFileService
{
    /// <summary>
    /// Baixa, no servidor, o arquivo (EPUB/PDF) da fonte externa e o devolve em
    /// bytes, para o front consumi-lo pela mesma origem (sem CORS). RF15/RF16.
    /// </summary>
    Task<PublicationFileResult> GetFileAsync(int publicationId, PublicationFileKind kind, CancellationToken ct = default);

    /// <summary>
    /// Baixa, no servidor, a capa da publicação (URL do Gutendex) e a devolve em
    /// bytes, para o front consumi-la pela mesma origem (sem CORS) e amostrar a cor
    /// dominante da lombada na estante (#27). `NotAvailable` quando a obra não tem capa.
    /// </summary>
    Task<PublicationFileResult> GetCoverAsync(int publicationId, CancellationToken ct = default);
}
