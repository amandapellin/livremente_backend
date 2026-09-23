namespace LivreMente.Api.Dtos;

/// <summary>Edição do perfil. Apenas o nome é editável (e-mail é somente-leitura no front).</summary>
public record UpdateProfileRequest(string? Name);
