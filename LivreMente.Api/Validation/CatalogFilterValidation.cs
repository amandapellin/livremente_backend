using LivreMente.Api.Mappings;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Validation;

/// <summary>
/// Filtros do catálogo (RF12) já validados e traduzidos para o vocabulário do
/// banco. `HasGenreFilter` distingue "sem filtro de gênero" de "filtro que não
/// casou nada". Listas vazias = filtro ausente.
/// </summary>
public sealed record CatalogFilters(
    PublicationType? Type,
    string[] Languages,
    string[] GenreBookNames,
    string[] GenreArchives,
    bool HasGenreFilter);

public static class CatalogFilterValidation
{
    private static readonly HashSet<string> Languages =
        new(StringComparer.Ordinal) { "pt", "en", "es", "fr", "ru" };

    public static (string? Error, CatalogFilters? Filters) Validate(
        string? type, IReadOnlyList<string>? languages, IReadOnlyList<string>? genres)
    {
        // type: enum fechado; aceita só os nomes do contrato (evita quirks de valor numérico).
        PublicationType? parsedType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            parsedType = type.Trim().ToLowerInvariant() switch
            {
                "book" => PublicationType.book,
                "scientific_article" => PublicationType.scientific_article,
                _ => null,
            };
            if (parsedType is null)
                return ($"Tipo inválido: '{type}'. Use book ou scientific_article.", null);
        }

        // languages: valida contra o vocabulário; OR dentro da lista (aplicado no serviço).
        var langs = new List<string>();
        foreach (var raw in languages ?? [])
        {
            var value = raw?.Trim().ToLowerInvariant() ?? "";
            if (value.Length == 0) continue;
            if (!Languages.Contains(value))
                return ($"Idioma inválido: '{raw}'.", null);
            if (!langs.Contains(value)) langs.Add(value);
        }

        // genres (slugs do front): traduz para nomes de gênero (livro) e archives (artigo).
        // Mesmo catálogo do #5/#11/#12; slug fora do catálogo → 400.
        var bookNames = new HashSet<string>(StringComparer.Ordinal);
        var archives = new HashSet<string>(StringComparer.Ordinal);
        var hasGenreFilter = false;
        foreach (var raw in genres ?? [])
        {
            var slug = raw?.Trim() ?? "";
            if (slug.Length == 0) continue;
            hasGenreFilter = true;

            if (PreferenceCatalog.BookGenres.TryGetValue(slug, out var names))
                foreach (var name in names) bookNames.Add(name);
            else if (PreferenceCatalog.ArticleArchives.TryGetValue(slug, out var arcs))
                foreach (var archive in arcs) archives.Add(archive);
            else
                return ($"Gênero inválido: '{slug}'.", null);
        }

        return (null, new CatalogFilters(
            parsedType, [.. langs], [.. bookNames], [.. archives], hasGenreFilter));
    }
}
