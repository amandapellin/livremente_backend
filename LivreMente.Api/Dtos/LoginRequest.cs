namespace LivreMente.Api.Dtos;

/// <summary>Credenciais de login. `RememberMe` controla a validade do refresh token.</summary>
public record LoginRequest(string? Email, string? Password, bool RememberMe);
