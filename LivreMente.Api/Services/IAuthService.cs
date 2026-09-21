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

public enum LoginError
{
    None,
    InvalidCredentials,
    EmailNotConfirmed,
}

/// <summary>Resultado do login: erro tipado + a resposta (quando sucesso).</summary>
public record LoginResult(LoginError Error, LoginResponse? Response = null);

public enum RefreshError
{
    None,
    Invalid,
}

/// <summary>Resultado da renovação: erro tipado + o novo par de tokens (quando sucesso).</summary>
public record RefreshResult(RefreshError Error, RefreshResponse? Response = null);

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<ConfirmResult> ConfirmAsync(string token, CancellationToken ct = default);

    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<RefreshResult> RefreshAsync(RefreshRequest request, CancellationToken ct = default);
}
