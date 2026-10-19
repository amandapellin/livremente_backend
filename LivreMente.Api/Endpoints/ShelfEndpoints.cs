using System.Security.Claims;
using LivreMente.Api.Dtos;
using LivreMente.Api.Models.Enums;
using LivreMente.Api.Security;
using LivreMente.Api.Services;

namespace LivreMente.Api.Endpoints;

/// <summary>
/// Estante do usuário (RF05/RF06, #32/#33). Autenticado; opera sobre a estante do
/// próprio usuário (identidade do JWT — RN04 por construção, sem `{userId}` na rota).
/// </summary>
public static class ShelfEndpoints
{
    // Estados válidos: nome do membro do enum = label do Postgres = slug do front.
    private static readonly Dictionary<string, ReadingStatus> AllowedStatuses = new(StringComparer.Ordinal)
    {
        ["read"] = ReadingStatus.read,
        ["reading"] = ReadingStatus.reading,
        ["want_to_read"] = ReadingStatus.want_to_read,
        ["abandoned"] = ReadingStatus.abandoned,
    };

    private const string StatusErrorMessage =
        "status inválido (use read, reading, want_to_read ou abandoned).";

    public static RouteGroupBuilder MapShelfEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shelf").WithTags("Shelf").RequireAuthorization();

        // RF05: lista a estante do usuário, opcionalmente filtrada por estado.
        group.MapGet("", async (
            ClaimsPrincipal principal, IShelfService shelf, CancellationToken ct, string? status = null) =>
        {
            // Identidade ANTES de validar a entrada (CodeQL cs/user-controlled-bypass).
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            ReadingStatus? filter = null;
            if (!string.IsNullOrEmpty(status))
            {
                if (!AllowedStatuses.TryGetValue(status, out var parsed))
                    return Results.BadRequest(new ErrorResponse(StatusErrorMessage, "VALIDATION_ERROR"));
                filter = parsed;
            }

            var items = await shelf.GetShelfAsync(userId.Value, filter, ct);
            return Results.Ok(items);
        });

        // RF06: upsert do estado (literal — define o estado enviado; preserva o
        // progresso). A regra de "não rebaixar 'lido'" vive no "Ler agora" do front.
        group.MapPut("/{publicationId:int}", async (
            int publicationId, UpdateShelfStatusRequest req, ClaimsPrincipal principal,
            IShelfService shelf, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            if (req?.Status is null || !AllowedStatuses.TryGetValue(req.Status, out var status))
                return Results.BadRequest(new ErrorResponse(StatusErrorMessage, "VALIDATION_ERROR"));

            var ok = await shelf.SetStatusAsync(userId.Value, publicationId, status, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // RF06: remove a obra da estante.
        group.MapDelete("/{publicationId:int}", async (
            int publicationId, ClaimsPrincipal principal, IShelfService shelf, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var removed = await shelf.RemoveAsync(userId.Value, publicationId, ct);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        return group;
    }
}
