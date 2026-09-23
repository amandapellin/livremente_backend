using LivreMente.Api.Dtos;

namespace LivreMente.Api.Validation;

/// <summary>Validação de forma da edição de perfil e da troca de senha.</summary>
public static class ProfileValidation
{
    public static string? ValidateUpdate(UpdateProfileRequest req)
    {
        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 120)
            return "Informe seu nome completo (2 a 120 caracteres).";
        return null;
    }

    public static string? ValidatePasswordChange(ChangePasswordRequest req)
    {
        if (string.IsNullOrEmpty(req.CurrentPassword))
            return "Informe a senha atual.";
        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 8 || req.NewPassword.Length > 128)
            return "A nova senha deve ter entre 8 e 128 caracteres.";
        return null;
    }
}
