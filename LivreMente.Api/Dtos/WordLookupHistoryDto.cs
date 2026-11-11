namespace LivreMente.Api.Dtos;

// Item do histórico de palavras consultadas (RF08). Inclui a obra de origem da
// consulta (RF07) e a definição consultada (#27, null em consultas antigas).
// `consultedAt` vem de `word_lookup.consult_date`.
public record WordLookupHistoryDto(string Word, DateTime ConsultedAt, int PublicationId, string PublicationTitle, string? Definition);
