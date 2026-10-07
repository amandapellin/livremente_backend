using LivreMente.Api.Dtos;
using LivreMente.Api.Services;
using LivreMente.Api.Validation;

namespace LivreMente.Api.Endpoints;

public static class PublicationEndpoints
{
    public static RouteGroupBuilder MapPublicationEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/publications")
            .WithTags("Publications");

        group.MapGet("", async (
            IPublicationService publications,
            CancellationToken ct,
            string? q = null,
            int page = 1,
            int pageSize = 10,
            string? sort = null) =>
        {
            return await ListAsync(publications, q, page, pageSize, sort, ct);
        })
        .AllowAnonymous()
        .Produces<CatalogPage>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/search", async (
            IPublicationService publications,
            CancellationToken ct,
            string? query = null,
            string? q = null,
            int page = 1,
            int pageSize = 10,
            string? sort = null) =>
        {
            return await ListAsync(publications, q ?? query, page, pageSize, sort, ct);
        })
        .AllowAnonymous()
        .WithSummary("Alias de GET /api/publications")
        .WithDescription("Mesmo contrato da listagem. Aceita query como alternativa a q; q tem precedência quando informado.")
        .Produces<CatalogPage>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> ListAsync(
        IPublicationService publications, string? q, int page, int pageSize,
        string? sort, CancellationToken ct)
    {
        var error = CatalogValidation.Validate(page, pageSize);
        if (error is not null)
            return Results.BadRequest(new ErrorResponse(error, "VALIDATION_ERROR"));

        return Results.Ok(await publications.ListAsync(q, page, pageSize, sort, ct));
    }
}
