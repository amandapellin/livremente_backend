namespace LivreMente.Api.Dtos;

// Consulta ao dicionário (RF07): a palavra e a publicação em leitura (para vincular
// o registro em word_lookup). O idioma da consulta deriva da publicação.
public record DictionaryLookupRequest(string Word, int PublicationId);
