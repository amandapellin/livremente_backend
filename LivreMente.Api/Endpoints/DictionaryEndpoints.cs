using System.Security.Claims;
using LivreMente.Api.Dtos;
using LivreMente.Api.Security;
using LivreMente.Api.Services;

namespace LivreMente.Api.Endpoints;

/// <summary>
/// Dicionário integrado (RF07/RF08). Autenticado; opera sobre o usuário do token
/// (RN04 por construção).
/// </summary>
public static class DictionaryEndpoints
{
    public static RouteGroupBuilder MapDictionaryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dictionary").WithTags("Dictionary").RequireAuthorization();

        // RF07: consulta o significado de uma palavra (fonte externa) sem sair da
        // leitura; registra em word_lookup (se save_dictionary), vinculada à publicação.
        group.MapPost("/lookup", async (
            DictionaryLookupRequest req, ClaimsPrincipal principal,
            IDictionaryService dictionary, CancellationToken ct) =>
        {
            // Identidade antes da validação (CodeQL cs/user-controlled-bypass).
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var word = req.Word?.Trim() ?? string.Empty;
            if (word.Length == 0 || word.Length > 100)
                return Results.BadRequest(new ErrorResponse("word é obrigatório e deve ter no máximo 100 caracteres.", "VALIDATION_ERROR"));

            var result = await dictionary.LookupAsync(userId.Value, req.PublicationId, word, ct);
            return result.Status switch
            {
                DictionaryLookupStatus.Ok => Results.Ok(result.Entry),
                DictionaryLookupStatus.WordNotFound => Results.Json(
                    new ErrorResponse("Palavra não encontrada no dicionário.", "WORD_NOT_FOUND"),
                    statusCode: StatusCodes.Status404NotFound),
                DictionaryLookupStatus.SourceUnavailable => Results.Json(
                    new ErrorResponse("Dicionário externo indisponível.", "DICTIONARY_UNAVAILABLE"),
                    statusCode: StatusCodes.Status502BadGateway),
                _ => Results.NotFound(), // PublicationNotFound
            };
        });

        // RF08: histórico de palavras consultadas pelo usuário (perfil), com a obra
        // de origem. `order` = `recent` (padrão, mais recentes primeiro) ou `oldest`.
        group.MapGet("/history", async (
            ClaimsPrincipal principal, IDictionaryService dictionary, string? order, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var descending = !string.Equals(order, "oldest", StringComparison.OrdinalIgnoreCase);
            var history = await dictionary.GetHistoryAsync(userId.Value, descending, ct);
            return Results.Ok(history);
        });

        return group;
    }
}
