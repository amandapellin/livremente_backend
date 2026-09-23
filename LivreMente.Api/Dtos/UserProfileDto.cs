namespace LivreMente.Api.Dtos;

/// <summary>
/// Perfil do usuário exposto pela API (nunca inclui `password_hash`).
/// `AvatarUrl` é o caminho para buscar a imagem (`/api/users/{id}/avatar`) quando
/// existe; `null` quando não há avatar (o front exibe as iniciais).
/// </summary>
public record UserProfileDto(string Id, string Name, string Email, string? AvatarUrl);
