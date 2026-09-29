using LivreMente.Api.Dtos;
using LivreMente.Api.Validation;

namespace LivreMente.Api.Services;

public interface IPreferenceService
{
    Task<UserPreferencesDto> GetPreferencesAsync(int userId, CancellationToken ct = default);

    Task<UserPreferencesDto> ReplacePreferencesAsync(int userId, NormalizedPreferences prefs, CancellationToken ct = default);
}