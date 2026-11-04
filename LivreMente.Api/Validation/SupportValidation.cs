using System.ComponentModel.DataAnnotations;
using LivreMente.Api.Dtos;

namespace LivreMente.Api.Validation;

/// <summary>Validação do formulário de contato do Suporte (#84).</summary>
public static class SupportValidation
{
    public const int MaxMessage = 5000;

    public static string? Validate(ContactRequest req)
    {
        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 120)
            return "Informe seu nome (2 a 120 caracteres).";

        var email = req.Email?.Trim();
        if (string.IsNullOrEmpty(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            return "E-mail inválido.";

        var subject = req.Subject?.Trim();
        if (string.IsNullOrEmpty(subject) || subject.Length > 150)
            return "Informe um assunto (até 150 caracteres).";

        var message = req.Message?.Trim();
        if (string.IsNullOrEmpty(message) || message.Length < 10 || message.Length > MaxMessage)
            return $"A mensagem deve ter entre 10 e {MaxMessage} caracteres.";

        return null;
    }
}
