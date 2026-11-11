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

        // RF27 (#37, endpoint de listagem): lista as recomendações do usuário autenticado,
        // delegando ao serviço da #36. Itens no mesmo card do catálogo (RF14), com `limit`.
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
        .WithSummary("Lista as recomendações personalizadas do usuário autenticado (RF27).")
        .WithDescription(
            $"Retorna até `limit` itens (1–{MaxLimit}, padrão 12) no mesmo card resumido do catálogo " +
            "(RF14), para reuso no front. `coldStart = true` quando a pessoa não tem perfil (lista por " +
            "popularidade). Obras já na estante são excluídas.")
        .Produces<RecommendationsDto>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        return group;
    }
}
