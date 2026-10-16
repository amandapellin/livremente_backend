using System.Security.Claims;
using LivreMente.Api.Dtos;
using LivreMente.Api.Security;
using LivreMente.Api.Services;

namespace LivreMente.Api.Endpoints;

/// <summary>
/// Endpoints do épico de Leitura (autenticados; operam sobre a estante do
/// próprio usuário, identidade do JWT — RN04 por construção).
/// </summary>
public static class ReadingEndpoints
{
    public static RouteGroupBuilder MapReadingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reading").WithTags("Reading").RequireAuthorization();

        // RF17/RN08: salvamento automático da última página lida (EPUB).
        group.MapPut("/{publicationId:int}/last-page", async (
            int publicationId, SaveLastPageRequest req, ClaimsPrincipal principal,
            IReadingService reading, CancellationToken ct) =>
        {
            // Resolve a identidade ANTES de validar a entrada: a autorização não
            // pode depender de valor controlado pelo usuário (CodeQL cs/user-controlled-bypass).
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            if (req.LastPageRead < 0)
                return Results.BadRequest(new ErrorResponse("lastPageRead deve ser maior ou igual a 0.", "VALIDATION_ERROR"));

            var result = await reading.SaveLastPageAsync(userId.Value, publicationId, req.LastPageRead, ct);
            return result switch
            {
                SaveLastPageResult.Saved => Results.NoContent(),
                SaveLastPageResult.NotEpub => Results.Json(
                    new ErrorResponse("A publicação não é EPUB; não há página a salvar.", "PUBLICATION_NOT_EPUB"),
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.NotFound(),
            };
        });

        // RF15/RF16: proxy dos arquivos de leitura. Público (conteúdo de acesso
        // aberto; a rota /leitura não é autenticada). O servidor busca na fonte
        // externa (sem CORS) e transmite pela mesma origem do front.
        group.MapGet("/{publicationId:int}/epub",
            (int publicationId, IReadingService reading, CancellationToken ct) =>
                ServeFileAsync(reading, publicationId, PublicationFileKind.Epub, ct))
            .AllowAnonymous();

        group.MapGet("/{publicationId:int}/pdf",
            (int publicationId, IReadingService reading, CancellationToken ct) =>
                ServeFileAsync(reading, publicationId, PublicationFileKind.Pdf, ct))
            .AllowAnonymous();

        return group;
    }

    private static async Task<IResult> ServeFileAsync(
        IReadingService reading, int publicationId, PublicationFileKind kind, CancellationToken ct)
    {
        var result = await reading.GetFileAsync(publicationId, kind, ct);
        return result.Status switch
        {
            GetFileStatus.Ok => Results.File(result.Content!, result.ContentType!, enableRangeProcessing: true),
            GetFileStatus.UpstreamError => Results.Json(
                new ErrorResponse("Falha ao obter o arquivo na fonte.", "UPSTREAM_ERROR"),
                statusCode: StatusCodes.Status502BadGateway),
            _ => Results.NotFound(),
        };
    }
}
