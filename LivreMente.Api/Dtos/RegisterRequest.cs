namespace LivreMente.Api.Dtos;

/// <summary>
/// Payload de cadastro (RF01). Espelha o contrato do front (client gerado via
/// OpenAPI): dados cadastrais + preferências opcionais + consentimento LGPD.
/// Campos chegam como string para validação explícita (ver RegisterValidation).
/// </summary>
public record RegisterRequest(
    string? Name,
    string? Email,
    string? Password,
    string? BirthDate,
    string? Gender,
    RegisterPreferences? Preferences,
    bool LgpdConsent,
    bool MarketingConsent);

/// <summary>Preferências de leitura para recomendação. Todas opcionais no cadastro.</summary>
public record RegisterPreferences(
    string[]? Languages,
    string[]? Publications,
    string[]? Categories,
    string[]? LiteraryGenres);
