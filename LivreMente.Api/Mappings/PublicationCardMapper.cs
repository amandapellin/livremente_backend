using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Mappings;

/// <summary>
/// Tradução nome→slug de gêneros/áreas para o card da publicação, compartilhada
/// pelo catálogo (#17/#20) e pela estante (#32) — evita duplicar a lógica de
/// slugs. Um nome de fonte pode corresponder a mais de um slug do front.
/// </summary>
public static class PublicationCardMapper
{
    private static readonly ILookup<string, string> BookGenreSlugs =
        PreferenceCatalog.BookGenres
            .SelectMany(entry => entry.Value.Select(name => (Name: name, Slug: entry.Key)))
            .ToLookup(entry => entry.Name, entry => entry.Slug, StringComparer.Ordinal);

    private static readonly ILookup<string, string> ArticleAreaSlugs =
        PreferenceCatalog.ArticleArchives
            .SelectMany(entry => entry.Value.Select(archive => (Archive: archive, Slug: entry.Key)))
            .ToLookup(entry => entry.Archive, entry => entry.Slug, StringComparer.Ordinal);

    /// <summary>
    /// Slugs do front para os gêneros/área da publicação (livro casa pela M:N
    /// `genre.name`; artigo casa pela área `KnowledgeArea`). Distintos e ordenados.
    /// </summary>
    public static IReadOnlyList<string> GenreSlugs(PublicationType type, IEnumerable<string> genreNames, string? knowledgeArea) =>
        (type == PublicationType.book
            ? genreNames.SelectMany(name => BookGenreSlugs[name])
            : ArticleAreaSlugs[PreferenceCatalog.ArchiveOf(knowledgeArea ?? "")])
        .Distinct().OrderBy(slug => slug).ToList();

    /// <summary>Capa do card: nula para artigo científico (só livro tem capa).</summary>
    public static string? CoverFor(PublicationType type, string? coverUrl) =>
        type == PublicationType.scientific_article ? null : coverUrl;
}
