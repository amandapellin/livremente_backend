namespace LivreMente.Api.Dtos;

/// <summary>Troca de senha: exige a senha atual e a nova senha.</summary>
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
