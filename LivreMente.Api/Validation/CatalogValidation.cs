namespace LivreMente.Api.Validation;

public static class CatalogValidation
{
    public static string? Validate(int page, int pageSize)
    {
        if (page < 1)
            return "A página deve ser maior ou igual a 1.";

        if (pageSize is < 1 or > 50)
            return "O tamanho da página deve estar entre 1 e 50.";

        return null;
    }
}
