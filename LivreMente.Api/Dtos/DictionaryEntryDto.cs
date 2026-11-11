namespace LivreMente.Api.Dtos;

// Significado devolvido ao front (RF07), normalizado a partir da fonte externa.
// Só o evento de consulta é persistido (word_lookup); o significado não é guardado.
public record DictionaryEntryDto(string Word, string? Phonetic, IReadOnlyList<DictionaryMeaningDto> Meanings);

public record DictionaryMeaningDto(string PartOfSpeech, IReadOnlyList<DictionaryDefinitionDto> Definitions);

public record DictionaryDefinitionDto(string Definition, string? Example);
