namespace LivreMente.Api.Dtos;

/// <summary>Resposta da renovação: novo par de tokens (o antigo refresh é revogado).</summary>
public record RefreshResponse(string Token, string RefreshToken);
