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
            string? type = null,
            string[]? languages = null,
            string[]? genres = null,
            int page = 1,
            int pageSize = 10,
            string? sort = null) =>
        {
            return await ListAsync(publications, q, type, languages, genres, page, pageSize, sort, ct);
        })
        .AllowAnonymous()
        .Produces<CatalogPage>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/search", async (
            IPublicationService publications,
            CancellationToken ct,
            string? query = null,
            string? q = null,
            string? type = null,
            string[]? languages = null,
            string[]? genres = null,
            int page = 1,
            int pageSize = 10,
            string? sort = null) =>
        {
            return await ListAsync(publications, q ?? query, type, languages, genres, page, pageSize, sort, ct);
        })
        .AllowAnonymous()
        .WithSummary("Alias de GET /api/publications")
        .WithDescription("Mesmo contrato da listagem. Aceita query como alternativa a q; q tem precedência quando informado.")
        .Produces<CatalogPage>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // Detalhes de uma publicação (RF13, #20). Público; 404 se não existir.
        group.MapGet("/{id:int}", async (int id, IPublicationService publications, CancellationToken ct) =>
        {
            var details = await publications.GetByIdAsync(id, ct);
            return details is null ? Results.NotFound() : Results.Ok(details);
        })
        .AllowAnonymous()
        .Produces<PublicationDetailsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> ListAsync(
        IPublicationService publications, string? q, string? type,
        string[]? languages, string[]? genres, int page, int pageSize,
        string? sort, CancellationToken ct)
    {
        var pageError = CatalogValidation.Validate(page, pageSize);
        if (pageError is not null)
            return Results.BadRequest(new ErrorResponse(pageError, "VALIDATION_ERROR"));

        var (filterError, filters) = CatalogFilterValidation.Validate(type, languages, genres);
        if (filterError is not null)
            return Results.BadRequest(new ErrorResponse(filterError, "VALIDATION_ERROR"));

        return Results.Ok(await publications.ListAsync(q, filters, page, pageSize, sort, ct));
    }
}
