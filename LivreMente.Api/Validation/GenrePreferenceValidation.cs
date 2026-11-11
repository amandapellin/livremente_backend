using LivreMente.Api.Dtos;
using LivreMente.Api.Mappings;

namespace LivreMente.Api.Validation;

public static class GenrePreferenceValidation
{
    public static (string? Error, IReadOnlyList<string> Slugs) Validate(UpdateGenresRequest req)
    {
        if (req.Genres is null)
            return ("Envie o campo genres (lista de slugs de gênero/categoria de livro).", []);

        var allowed = PreferenceCatalog.BookGenres.Keys;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in req.Genres)
        {
            var slug = item?.Trim() ?? "";
            if (slug.Length == 0)
                return ("Valor vazio não é permitido em genres.", []);
            if (!allowed.Contains(slug))
                return ($"Gênero inválido: '{slug}'.", []);

            if (seen.Add(slug))
                result.Add(slug);
        }

        return (null, result);
    }
}
