using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Dtos;
using LivreMente.Api.Mappings;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;
using LivreMente.Api.Validation;

namespace LivreMente.Api.Services;

public class PreferenceService(LivreMenteDbContext db) : IPreferenceService
{
    private readonly LivreMenteDbContext _db = db;

    public async Task<UserPreferencesDto> GetPreferencesAsync(int userId, CancellationToken ct = default)
    {
        var rows = await _db.UserPreferences
            .Where(p => p.UserId == userId)
            .Select(p => new { p.PreferenceType, p.PreferenceValue })
            .ToListAsync(ct);

        IReadOnlyList<string> Of(PreferenceType type) =>
            [.. rows.Where(r => r.PreferenceType == type)
                .Select(r => r.PreferenceValue)
                .OrderBy(v => v, StringComparer.Ordinal)];

        var storedAreas = rows
            .Where(r => r.PreferenceType == PreferenceType.knowledge_area)
            .Select(r => r.PreferenceValue)
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<string> knowledgeAreaSlugs =
            [.. PreferenceCatalog.ArticleArchives
                .Where(kv => kv.Value.Length > 0 && kv.Value.All(a => storedAreas.Contains(a)))
                .Select(kv => kv.Key)
                .OrderBy(k => k, StringComparer.Ordinal)];

        return new UserPreferencesDto(
            Of(PreferenceType.language),
            Of(PreferenceType.content_type),
            knowledgeAreaSlugs);
    }

    public async Task<UserPreferencesDto> ReplacePreferencesAsync(
        int userId, NormalizedPreferences prefs, CancellationToken ct = default)
    {
        var types = prefs.ByType.Keys.ToList();

        // Carrega só as linhas dos tipos que serão substituídos.
        var current = await _db.UserPreferences
            .Where(p => p.UserId == userId && types.Contains(p.PreferenceType))
            .ToListAsync(ct);

        foreach (var (type, desiredValues) in prefs.ByType)
        {
            var desired = desiredValues.ToHashSet(StringComparer.Ordinal);
            var rowsOfType = current.Where(p => p.PreferenceType == type).ToList();

            // Remove o que saiu da seleção.
            foreach (var row in rowsOfType.Where(p => !desired.Contains(p.PreferenceValue)))
                _db.UserPreferences.Remove(row);

            // Adiciona só o que entrou; o que já existe NÃO é tocado.
            var existingValues = rowsOfType.Select(p => p.PreferenceValue).ToHashSet(StringComparer.Ordinal);
            foreach (var value in desired.Where(v => !existingValues.Contains(v)))
                _db.UserPreferences.Add(new UserPreference
                {
                    UserId = userId,
                    PreferenceType = type,
                    PreferenceValue = value,
                });
        }

        await _db.SaveChangesAsync(ct);
        return await GetPreferencesAsync(userId, ct);
    }

    public async Task<UserGenresDto> GetGenresAsync(int userId, CancellationToken ct = default)
    {
        var names = await _db.Users
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Genres.Select(g => g.Name))
            .ToListAsync(ct);

        var stored = names.ToHashSet(StringComparer.Ordinal);

        // Tradução reversa: um slug entra se TODOS os seus genre.names estão
        // presentes — simétrico ao fan-out gravado no PUT/cadastro. As listas do
        // catálogo são ~disjuntas, então a reversão é determinística.
        IReadOnlyList<string> slugs =
            [.. PreferenceCatalog.BookGenres
                .Where(kv => kv.Value.Length > 0 && kv.Value.All(n => stored.Contains(n)))
                .Select(kv => kv.Key)
                .OrderBy(k => k, StringComparer.Ordinal)];

        return new UserGenresDto(slugs);
    }

    public async Task<UserGenresDto> ReplaceGenresAsync(
        int userId, IReadOnlyList<string> slugs, CancellationToken ct = default)
    {
        // slug → genre.name (fan-out via catálogo), consistente com o cadastro (#5).
        var desiredNames = slugs
            .SelectMany(s => PreferenceCatalog.BookGenres.TryGetValue(s, out var names) ? names : [])
            .ToHashSet(StringComparer.Ordinal);

        var user = await _db.Users
            .Include(u => u.Genres)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return new UserGenresDto([]);

        var desired = desiredNames.Count == 0
            ? []
            : await _db.Genres.Where(g => desiredNames.Contains(g.Name)).ToListAsync(ct);

        // Substitui por diff: remove o que saiu, adiciona o que entrou; o EF
        // traduz isso em DELETE/INSERT nas linhas de user_genre (PK user_id+genre_id).
        var desiredIds = desired.Select(g => g.Id).ToHashSet();
        var currentIds = user.Genres.Select(g => g.Id).ToHashSet();

        foreach (var genre in user.Genres.Where(g => !desiredIds.Contains(g.Id)).ToList())
            user.Genres.Remove(genre);
        foreach (var genre in desired.Where(g => !currentIds.Contains(g.Id)))
            user.Genres.Add(genre);

        await _db.SaveChangesAsync(ct);
        return await GetGenresAsync(userId, ct);
    }

    public async Task<bool> RemoveGenreAsync(int userId, int genreId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.Genres)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        var genre = user?.Genres.FirstOrDefault(g => g.Id == genreId);
        if (genre is null)
            return false;

        user!.Genres.Remove(genre);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}