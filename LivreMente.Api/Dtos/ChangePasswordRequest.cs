namespace LivreMente.Api.Dtos;

public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
