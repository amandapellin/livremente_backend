using LivreMente.Api.Dtos;
using LivreMente.Api.Validation;

namespace LivreMente.Api.Services;

public interface IPreferenceService
{
    Task<UserPreferencesDto> GetPreferencesAsync(int userId, CancellationToken ct = default);

    Task<UserPreferencesDto> ReplacePreferencesAsync(int userId, NormalizedPreferences prefs, CancellationToken ct = default);


    Task<UserGenresDto> GetGenresAsync(int userId, CancellationToken ct = default);

    Task<UserGenresDto> ReplaceGenresAsync(int userId, IReadOnlyList<string> slugs, CancellationToken ct = default);

    Task<bool> RemoveGenreAsync(int userId, int genreId, CancellationToken ct = default);
}