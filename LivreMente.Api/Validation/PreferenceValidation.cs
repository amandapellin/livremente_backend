using LivreMente.Api.Dtos;
using LivreMente.Api.Mappings;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Validation;

public sealed record NormalizedPreferences(
    IReadOnlyDictionary<PreferenceType, IReadOnlyList<string>> ByType,
    string? Theme,
    bool? ResumeAuto,
    bool? SaveDictionary);

public static class PreferenceValidation
{
    private static readonly HashSet<string> ContentTypes =
        new(StringComparer.Ordinal) { "book", "scientific_article" };
    private static readonly HashSet<string> Languages =
        new(StringComparer.Ordinal) { "pt", "en", "es", "fr", "ru" };
    private static readonly HashSet<string> Themes =
        new(StringComparer.Ordinal) { "light", "sepia", "dark" };

    public static (string? Error, NormalizedPreferences? Prefs) Validate(UpdatePreferencesRequest req)
    {
        var byType = new Dictionary<PreferenceType, IReadOnlyList<string>>();

        if (req.Languages is not null)
        {
            var (err, values) = Clean(PreferenceType.language, req.Languages, Languages, toLower: true);
            if (err is not null) return (err, null);
            byType[PreferenceType.language] = values;
        }

        if (req.ContentTypes is not null)
        {
            var (err, values) = Clean(PreferenceType.content_type, req.ContentTypes, ContentTypes, toLower: true);
            if (err is not null) return (err, null);
            byType[PreferenceType.content_type] = values;
        }

        if (req.KnowledgeAreas is not null)
        {
            var allowed = PreferenceCatalog.ArticleArchives.Keys.ToHashSet(StringComparer.Ordinal);
            var (err, slugs) = Clean(PreferenceType.knowledge_area, req.KnowledgeAreas, allowed, toLower: false);
            if (err is not null) return (err, null);

            var archives = slugs
                .SelectMany(slug => PreferenceCatalog.ArticleArchives[slug])
                .Distinct(StringComparer.Ordinal)
                .ToList();
            byType[PreferenceType.knowledge_area] = archives;
        }

        // Campos do leitor (RF26): escalares, merge parcial. null = não enviado.
        string? theme = null;
        if (req.Theme is not null)
        {
            theme = req.Theme.Trim().ToLowerInvariant();
            if (!Themes.Contains(theme))
                return ($"Tema inválido: '{req.Theme}'. Use light, sepia ou dark.", null);
        }

        var nothingSent = byType.Count == 0
            && req.Theme is null && req.ResumeAuto is null && req.SaveDictionary is null;
        if (nothingSent)
            return ("Envie ao menos um campo: languages, contentTypes, knowledgeAreas, theme, resumeAuto ou saveDictionary.", null);

        return (null, new NormalizedPreferences(byType, theme, req.ResumeAuto, req.SaveDictionary));
    }

    private static (string? Error, IReadOnlyList<string> Values) Clean(
        PreferenceType type, IReadOnlyList<string> raw, ISet<string> allowed, bool toLower)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in raw)
        {
            var value = item?.Trim() ?? "";
            if (toLower) value = value.ToLowerInvariant();

            if (value.Length == 0)
                return ($"Valor vazio não é permitido em {type}.", result);
            if (!allowed.Contains(value))
                return ($"Valor inválido para {type}: '{value}'.", result);

            if (seen.Add(value))  
                result.Add(value);
        }

        return (null, result);
    }
}