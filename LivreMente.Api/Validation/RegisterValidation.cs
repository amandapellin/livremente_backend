using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LivreMente.Api.Dtos;
using LivreMente.Api.Mappings;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Validation;

/// <summary>
/// Validação leve do cadastro, espelhando o Zod do front. Retorna a primeira
/// mensagem de erro encontrada, ou null se o payload é válido.
/// </summary>
public static class RegisterValidation
{
    private static readonly string[] AllowedLanguages = ["pt", "en", "es", "fr", "ru"];

    public static string? Validate(RegisterRequest req)
    {
        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 120)
            return "Informe seu nome completo (2 a 120 caracteres).";

        var email = req.Email?.Trim();
        if (string.IsNullOrEmpty(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            return "E-mail inválido.";

        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 8 || req.Password.Length > 128)
            return "A senha deve ter entre 8 e 128 caracteres.";

        if (string.IsNullOrEmpty(req.BirthDate)
            || !DateOnly.TryParse(req.BirthDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth))
            return "Data de nascimento inválida.";
        if (birth > DateOnly.FromDateTime(DateTime.UtcNow))
            return "Data de nascimento não pode ser futura.";

        if (string.IsNullOrEmpty(req.Gender) || !Enum.GetNames<Gender>().Contains(req.Gender))
            return "Gênero inválido.";

        if (!req.LgpdConsent)
            return "É necessário aceitar o tratamento de dados (LGPD) para criar a conta.";

        var prefsError = ValidatePreferences(req.Preferences);
        if (prefsError is not null)
            return prefsError;

        return null;
    }

    private static string? ValidatePreferences(RegisterPreferences? prefs)
    {
        if (prefs is null)
            return null;

        foreach (var lang in prefs.Languages ?? [])
            if (!AllowedLanguages.Contains(lang))
                return $"Idioma inválido: {lang}.";

        foreach (var pub in prefs.Publications ?? [])
            if (!Enum.GetNames<PublicationType>().Contains(pub))
                return $"Tipo de publicação inválido: {pub}.";

        foreach (var slug in (prefs.Categories ?? []).Concat(prefs.LiteraryGenres ?? []))
            if (!PreferenceCatalog.BookGenres.ContainsKey(slug) && !PreferenceCatalog.ArticleArchives.ContainsKey(slug))
                return $"Categoria/gênero inválido: {slug}.";

        return null;
    }
}
