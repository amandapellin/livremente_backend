namespace LivreMente.Api.Dtos;

public record UserConsentDto(
    bool LgpdConsent,
    bool MarketingConsent,
    DateTime? ConsentedAt
);
