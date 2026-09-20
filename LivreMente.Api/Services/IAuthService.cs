using LivreMente.Api.Dtos;

namespace LivreMente.Api.Services;

public enum RegisterError
{
    None,
    EmailAlreadyExists,
}

/// <summary>Resultado do cadastro (Result object — evita exceção para caso previsto).</summary>
public record RegisterResult(RegisterError Error, int? UserId = null);

public enum ConfirmResult
{
    Confirmed,
    InvalidOrExpired,
}

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<ConfirmResult> ConfirmAsync(string token, CancellationToken ct = default);
}
