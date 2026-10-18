using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Dtos;
using LivreMente.Api.Models;

namespace LivreMente.Api.Services;

/// <summary>
/// Consulta ao dicionário (RF07). Fonte externa: Free Dictionary API
/// (api.dictionaryapi.dev). Opera sempre sobre o usuário do token (RN04).
/// </summary>
public class DictionaryService(
    LivreMenteDbContext db, IHttpClientFactory httpClientFactory) : IDictionaryService
{
    private readonly LivreMenteDbContext _db = db;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Idiomas suportados pela fonte externa (código do catálogo → código da API).
    // O catálogo é dominado por `en`; idioma vazio/não suportado cai no fallback `en`.
    private static readonly Dictionary<string, string> DictLang = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en", ["fr"] = "fr", ["de"] = "de", ["es"] = "es", ["it"] = "it",
        ["pt"] = "pt-BR", ["ru"] = "ru", ["ja"] = "ja", ["ko"] = "ko", ["ar"] = "ar",
        ["tr"] = "tr", ["hi"] = "hi",
    };

    public async Task<DictionaryLookupResult> LookupAsync(
        int userId, int publicationId, string word, CancellationToken ct = default)
    {
        var pub = await _db.Publications.AsNoTracking()
            .Where(p => p.Id == publicationId)
            .Select(p => new { p.Language })
            .FirstOrDefaultAsync(ct);
        if (pub is null)
            return new DictionaryLookupResult(DictionaryLookupStatus.PublicationNotFound, null);

        var (status, entry) = await FetchAsync(MapLanguage(pub.Language), word, ct);
        if (status != DictionaryLookupStatus.Ok || entry is null)
            return new DictionaryLookupResult(status, null);

        // RF26: só registra a consulta se o usuário permite (users.save_dictionary).
        var save = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SaveDictionary)
            .FirstOrDefaultAsync(ct);
        if (save)
        {
            _db.WordLookups.Add(new WordLookup { UserId = userId, PublicationId = publicationId, Word = word });
            await _db.SaveChangesAsync(ct); // consult_date tem default no banco
        }

        return new DictionaryLookupResult(DictionaryLookupStatus.Ok, entry);
    }

    private static string MapLanguage(string? language) =>
        language is not null && DictLang.TryGetValue(language, out var mapped) ? mapped : "en";

    private async Task<(DictionaryLookupStatus Status, DictionaryEntryDto? Entry)> FetchAsync(
        string lang, string word, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("dictionary");
        try
        {
            using var resp = await http.GetAsync($"api/v2/entries/{lang}/{Uri.EscapeDataString(word)}", ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return (DictionaryLookupStatus.WordNotFound, null);
            if (!resp.IsSuccessStatusCode)
                return (DictionaryLookupStatus.SourceUnavailable, null);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var entries = await JsonSerializer.DeserializeAsync<List<FreeDictEntry>>(stream, JsonOpts, ct);
            var dto = MapEntry(word, entries);
            return dto is null ? (DictionaryLookupStatus.WordNotFound, null) : (DictionaryLookupStatus.Ok, dto);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return (DictionaryLookupStatus.SourceUnavailable, null);
        }
    }

    // Normaliza a resposta da fonte: junta verbetes (homógrafos), limita a 6 acepções
    // e 3 definições cada, descarta vazias. Devolve null se não houver acepção útil.
    private static DictionaryEntryDto? MapEntry(string word, List<FreeDictEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
            return null;

        var phonetic = entries.Select(e => e.Phonetic).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        var meanings = entries
            .Where(e => e.Meanings is not null)
            .SelectMany(e => e.Meanings!)
            .Select(m => new DictionaryMeaningDto(
                m.PartOfSpeech ?? string.Empty,
                (m.Definitions ?? [])
                    .Where(d => !string.IsNullOrWhiteSpace(d.Definition))
                    .Take(3)
                    .Select(d => new DictionaryDefinitionDto(d.Definition!, string.IsNullOrWhiteSpace(d.Example) ? null : d.Example))
                    .ToList()))
            .Where(m => m.Definitions.Count > 0)
            .Take(6)
            .ToList();

        if (meanings.Count == 0)
            return null;

        return new DictionaryEntryDto(
            entries[0].Word ?? word,
            string.IsNullOrWhiteSpace(phonetic) ? null : phonetic,
            meanings);
    }

    // Shape da fonte externa (Free Dictionary API) para desserialização.
    private sealed record FreeDictEntry(string? Word, string? Phonetic, List<FreeDictMeaning>? Meanings);
    private sealed record FreeDictMeaning(string? PartOfSpeech, List<FreeDictDefinition>? Definitions);
    private sealed record FreeDictDefinition(string? Definition, string? Example);
}
