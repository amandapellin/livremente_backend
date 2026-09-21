using System.ComponentModel.DataAnnotations;
using LivreMente.Api.Dtos;

namespace LivreMente.Api.Validation;

/// <summary>
/// Validação de forma do login (não de credenciais). Só garante que os campos
/// vieram e têm formato plausível — credenciais que "não batem" são 401, não 400.
/// </summary>
public static class LoginValidation
{
    public static string? Validate(LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || req.Email.Length > 254 || !new EmailAddressAttribute().IsValid(req.Email))
            return "E-mail inválido.";

        if (string.IsNullOrEmpty(req.Password) || req.Password.Length > 128)
            return "Senha inválida.";

        return null;
    }
}
