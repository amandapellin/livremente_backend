namespace LivreMente.Api.Dtos;

public record AuthUserDto(string Id, string Name, string Email);

public record LoginResponse(string Token, string RefreshToken, AuthUserDto User);
