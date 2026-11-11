using LivreMente.Api.Dtos;

namespace LivreMente.Api.Services;

public enum DictionaryLookupStatus
{
    Ok,
    PublicationNotFound,
    WordNotFound,       // a fonte respondeu, mas não há verbete para a palavra
    SourceUnavailable,  // falha ao consultar a fonte externa (rede/erro)
}

public record DictionaryLookupResult(DictionaryLookupStatus Status, DictionaryEntryDto? Entry);

public interface IDictionaryService
{
    /// <summary>
    /// Consulta o significado de uma palavra (RF07) numa fonte externa, usando o
    /// idioma da publicação (fallback `en`), e — se o usuário permite
    /// (`save_dictionary`) — registra a consulta em `word_lookup` vinculada à
    /// publicação. Não guarda o significado; só devolve. Publicação inexistente →
    /// PublicationNotFound; sem verbete → WordNotFound; falha na fonte → SourceUnavailable.
    /// </summary>
    Task<DictionaryLookupResult> LookupAsync(int userId, int publicationId, string word, CancellationToken ct = default);

    /// <summary>
    /// Histórico de palavras consultadas pelo usuário (RF08), uma entrada por
    /// consulta, incluindo a obra de origem. Ordenado por data de consulta —
    /// `descending = true` (mais recentes primeiro) ou falso (mais antigas primeiro).
    /// </summary>
    Task<IReadOnlyList<Dtos.WordLookupHistoryDto>> GetHistoryAsync(int userId, bool descending, CancellationToken ct = default);
}
