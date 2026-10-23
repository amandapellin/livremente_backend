using System.Security.Claims;
using LivreMente.Api.Dtos;
using LivreMente.Api.Security;
using LivreMente.Api.Services;

namespace LivreMente.Api.Endpoints;

public static class RecommendationEndpoints
{
    private const int MaxLimit = 50;

    public static RouteGroupBuilder MapRecommendationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recommendations").WithTags("Recommendations").RequireAuthorization();

        // RF27 (#36): recomendações personalizadas do usuário (preferências + histórico).
        group.MapGet("", async (
            ClaimsPrincipal principal, IRecommendationService recommendations, CancellationToken ct,
            int limit = 12) =>
        {
            // Identidade do JWT antes de validar a entrada (CodeQL cs/user-controlled-bypass).
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            if (limit < 1 || limit > MaxLimit)
                return Results.BadRequest(new ErrorResponse($"limit deve estar entre 1 e {MaxLimit}.", "VALIDATION_ERROR"));

            var result = await recommendations.GetRecommendationsAsync(userId.Value, limit, ct);
            return Results.Ok(result);
        })
        .Produces<RecommendationsDto>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        return group;
    }
}
