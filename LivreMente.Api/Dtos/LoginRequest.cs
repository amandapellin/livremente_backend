namespace LivreMente.Api.Dtos;

public record LoginRequest(string? Email, string? Password, bool RememberMe);
