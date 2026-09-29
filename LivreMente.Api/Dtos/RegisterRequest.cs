namespace LivreMente.Api.Dtos;

public record RegisterRequest(
    string? Name,
    string? Email,
    string? Password,
    string? BirthDate,
    string? Gender,
    RegisterPreferences? Preferences,
    bool LgpdConsent,
    bool MarketingConsent);

public record RegisterPreferences(
    string[]? Languages,
    string[]? Publications,
    string[]? Categories,
    string[]? LiteraryGenres);
