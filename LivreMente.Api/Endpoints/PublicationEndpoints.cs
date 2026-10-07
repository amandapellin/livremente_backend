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
            var error = CatalogValidation.Validate(page, pageSize);
            if (error is not null)
                return Results.BadRequest(new ErrorResponse(error, "VALIDATION_ERROR"));

            return Results.Ok(await publications.ListAsync(q, page, pageSize, sort, ct));
        })
        .AllowAnonymous()
        .Produces<CatalogPage>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/search", async (
            string query,
            IPublicationService publications,
            CancellationToken ct,
            int page = 1,
            int pageSize = 20) =>
        {
            var result = await publications.SearchAsync(
                query, page, pageSize, ct);

            return Results.Ok(result);
        })
        .Produces<PagedResult<PublicationSummaryDto>>(
            StatusCodes.Status200OK);

        return group;
    }
}
