namespace LivreMente.Api.Dtos;

/// <summary>Dados mínimos do usuário retornados ao autenticar (sem `password_hash`).</summary>
public record AuthUserDto(string Id, string Name, string Email);

/// <summary>Resposta de login: token de acesso (JWT), refresh token e o usuário.</summary>
public record LoginResponse(string Token, string RefreshToken, AuthUserDto User);
