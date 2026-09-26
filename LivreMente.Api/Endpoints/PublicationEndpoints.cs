using LivreMente.Api.Dtos;
using LivreMente.Api.Services;

namespace LivreMente.Api.Endpoints;

public static class PublicationEndpoints
{
    public static RouteGroupBuilder MapPublicationEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/publications")
            .WithTags("Publications");

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