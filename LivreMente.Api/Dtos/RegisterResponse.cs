namespace LivreMente.Api.Dtos;

/// <summary>Resposta 201 do cadastro. Nunca inclui o hash de senha.</summary>
public record RegisterResponse(string Id, string Name, string Email);
