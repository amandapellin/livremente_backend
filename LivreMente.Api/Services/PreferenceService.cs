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

        // knowledge_area é gravado como archives do arXiv (traduzido no PUT). No GET,
        // devolve os slugs de área do front (tradução reversa): um slug entra se TODOS
        // os seus archives estão presentes — simétrico ao fan-out gravado.
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
}