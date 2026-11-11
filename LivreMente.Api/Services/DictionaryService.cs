using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Dtos;
using LivreMente.Api.Models;

namespace LivreMente.Api.Services;

/// <summary>
/// Consulta ao dicionário (RF07). Fonte primária: Free Dictionary API
/// (api.dictionaryapi.dev). Quando ela está indisponível, cai para um
/// <b>fallback</b>: a Wiktionary REST da Wikimedia (en.wiktionary.org), mais
/// estável, que devolve a palavra agrupada por idioma. Opera sempre sobre o
/// usuário do token (RN04).
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

        // Fonte primária indisponível → tenta o fallback (Wiktionary). Só nesse
        // caso: 404 (sem verbete) e sucesso permanecem como estão, para não trocar
        // a semântica nem a qualidade quando a primária respondeu.
        if (status == DictionaryLookupStatus.SourceUnavailable)
        {
            var fallback = await FetchFallbackAsync(pub.Language, word, ct);
            if (fallback is not null)
                (status, entry) = (DictionaryLookupStatus.Ok, fallback);
        }

        if (status != DictionaryLookupStatus.Ok || entry is null)
            return new DictionaryLookupResult(status, null);

        // RF26: só registra a consulta se o usuário permite (users.save_dictionary).
        var save = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SaveDictionary)
            .FirstOrDefaultAsync(ct);
        if (save)
        {
            // Guarda a 1ª acepção (RF08, #27) para o card do histórico; trunca ao
            // limite da coluna (varchar(500)).
            var definition = entry.Meanings
                .SelectMany(m => m.Definitions)
                .Select(d => d.Definition)
                .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
            if (definition is { Length: > 500 })
                definition = definition[..500];

            _db.WordLookups.Add(new WordLookup
            {
                UserId = userId,
                PublicationId = publicationId,
                Word = word,
                Definition = definition,
            });
            await _db.SaveChangesAsync(ct); // consult_date tem default no banco
        }

        return new DictionaryLookupResult(DictionaryLookupStatus.Ok, entry);
    }

    public async Task<IReadOnlyList<WordLookupHistoryDto>> GetHistoryAsync(
        int userId, bool descending, CancellationToken ct = default)
    {
        // Uma entrada por consulta (RF08); join na publicação para a obra de origem.
        // Desempate por Id na mesma ordem para estabilidade.
        var query = _db.WordLookups.AsNoTracking().Where(w => w.UserId == userId);
        query = descending
            ? query.OrderByDescending(w => w.ConsultDate).ThenByDescending(w => w.Id)
            : query.OrderBy(w => w.ConsultDate).ThenBy(w => w.Id);

        return await query
            .Select(w => new WordLookupHistoryDto(w.Word, w.ConsultDate, w.PublicationId, w.Publication.Title, w.Definition))
            .ToListAsync(ct);
    }

    private static string MapLanguage(string? language) =>
        language is not null && DictLang.TryGetValue(language, out var mapped) ? mapped : "en";

    private async Task<(DictionaryLookupStatus Status, DictionaryEntryDto? Entry)> FetchAsync(
        string lang, string word, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("dictionary");
        var path = $"api/v2/entries/{lang}/{Uri.EscapeDataString(word)}";

        // A fonte (Free Dictionary API) é gratuita e instável: falhas transitórias
        // — 5xx (inclusive o 530/"error 1033" do Cloudflare), timeouts e quedas de
        // conexão — ocorrem de forma intermitente para a MESMA palavra. Re-tenta
        // algumas vezes antes de desistir (melhora muito a taxa de sucesso).
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var resp = await http.GetAsync(path, ct);
                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return (DictionaryLookupStatus.WordNotFound, null);
                if (!resp.IsSuccessStatusCode)
                {
                    // 5xx é transitório → re-tenta; qualquer outro status não-ok, desiste.
                    if ((int)resp.StatusCode >= 500 && attempt < maxAttempts)
                    {
                        await Task.Delay(250 * attempt, ct);
                        continue;
                    }
                    return (DictionaryLookupStatus.SourceUnavailable, null);
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var entries = await JsonSerializer.DeserializeAsync<List<FreeDictEntry>>(stream, JsonOpts, ct);
                var dto = MapEntry(word, entries);
                return dto is null ? (DictionaryLookupStatus.WordNotFound, null) : (DictionaryLookupStatus.Ok, dto);
            }
            // Falha de rede/timeout: re-tenta enquanto houver tentativa; senão, desiste.
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                await Task.Delay(250 * attempt, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                return (DictionaryLookupStatus.SourceUnavailable, null);
            }
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

    // Idiomas com bloco na Wiktionary (a chave da resposta é o código ISO).
    // Idioma vazio/não mapeado cai em "en".
    private static readonly HashSet<string> WiktLangs = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "fr", "de", "es", "it", "pt", "ru", "ja", "ko", "ar", "tr", "hi",
    };

    private static string WiktBlock(string? language) =>
        language is not null && WiktLangs.Contains(language) ? language.ToLowerInvariant() : "en";

    // Fallback: Wiktionary REST (Wikimedia). O endpoint de definição existe no
    // en.wiktionary e devolve a palavra definida em vários idiomas (glosas em
    // inglês), agrupados por código. Escolhe o bloco do idioma da obra; se vazio/
    // ausente, cai para "en". As definições vêm em HTML — limpa as tags. Não há
    // fonética neste endpoint. Qualquer falha → null (quem chama mantém o erro da
    // fonte primária).
    private async Task<DictionaryEntryDto?> FetchFallbackAsync(string? language, string word, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("dictionary-fallback");
        try
        {
            using var resp = await http.GetAsync($"api/rest_v1/page/definition/{Uri.EscapeDataString(word)}", ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var blocks = await JsonSerializer.DeserializeAsync<Dictionary<string, List<WikGloss>>>(stream, JsonOpts, ct);
            if (blocks is null || blocks.Count == 0)
                return null;

            // Bloco do idioma da obra; se vazio/ausente, "en"; senão, o primeiro
            // bloco de idioma real (ignora o agrupador "other").
            var chosen = Pick(blocks, WiktBlock(language)) ?? Pick(blocks, "en")
                ?? blocks.FirstOrDefault(kv => !kv.Key.Equals("other", StringComparison.OrdinalIgnoreCase)).Value;

            return MapFallbackEntry(word, chosen);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static List<WikGloss>? Pick(Dictionary<string, List<WikGloss>> blocks, string key) =>
        blocks.TryGetValue(key, out var v) && v is { Count: > 0 } ? v : null;

    // Mapeia um bloco da Wiktionary (lista de acepções) para o DTO: limpa o HTML
    // das definições, limita a 6 acepções e 3 definições cada, descarta vazias.
    private static DictionaryEntryDto? MapFallbackEntry(string word, List<WikGloss>? glosses)
    {
        if (glosses is null || glosses.Count == 0)
            return null;

        var meanings = glosses
            .Select(g => new DictionaryMeaningDto(
                g.PartOfSpeech ?? string.Empty,
                (g.Definitions ?? [])
                    .Select(d => new { Text = StripHtml(d.Definition), Example = FirstExample(d) })
                    .Where(d => !string.IsNullOrWhiteSpace(d.Text))
                    .Take(3)
                    .Select(d => new DictionaryDefinitionDto(d.Text, d.Example))
                    .ToList()))
            .Where(m => m.Definitions.Count > 0)
            .Take(6)
            .ToList();

        return meanings.Count == 0 ? null : new DictionaryEntryDto(word, null, meanings);
    }

    private static string? FirstExample(WikDef d)
    {
        var ex = d.ParsedExamples?
            .Select(e => StripHtml(e.Example))
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        return string.IsNullOrWhiteSpace(ex) ? null : ex;
    }

    // Remove tags HTML, decodifica entidades e colapsa espaços (as definições da
    // Wiktionary vêm com <a>/<i>/etc.).
    private static string StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;
        // Remove blocos <style>/<script> inteiros antes das tags: alguns templates
        // da Wiktionary (química/matemática) embutem CSS que vazaria como texto.
        var text = Regex.Replace(html, "<(style|script)[^>]*>.*?</\\1>", string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    // Shape da fonte primária (Free Dictionary API) para desserialização.
    private sealed record FreeDictEntry(string? Word, string? Phonetic, List<FreeDictMeaning>? Meanings);
    private sealed record FreeDictMeaning(string? PartOfSpeech, List<FreeDictDefinition>? Definitions);
    private sealed record FreeDictDefinition(string? Definition, string? Example);

    // Shape do fallback (Wiktionary REST) para desserialização.
    private sealed record WikGloss(string? PartOfSpeech, string? Language, List<WikDef>? Definitions);
    private sealed record WikDef(string? Definition, List<WikExample>? ParsedExamples);
    private sealed record WikExample(string? Example);
}
