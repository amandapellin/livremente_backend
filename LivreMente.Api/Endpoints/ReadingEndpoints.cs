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

        // RF20: abre uma sessão de leitura (EPUB e PDF) → id da sessão.
        group.MapPost("/{publicationId:int}/session", async (
            int publicationId, ClaimsPrincipal principal, IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var sessionId = await reading.StartSessionAsync(userId.Value, publicationId, ct);
            return sessionId is null
                ? Results.NotFound()
                : Results.Ok(new StartSessionResponse(sessionId.Value));
        });

        // RF20: heartbeat/encerramento — grava o tempo ativo (segundos) da sessão.
        group.MapPut("/sessions/{sessionId:int}", async (
            int sessionId, UpdateSessionRequest req, ClaimsPrincipal principal,
            IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            if (req.ActiveSeconds < 0)
                return Results.BadRequest(new ErrorResponse("activeSeconds deve ser maior ou igual a 0.", "VALIDATION_ERROR"));

            var ok = await reading.UpdateSessionAsync(userId.Value, sessionId, req.ActiveSeconds, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // RF19: atualização do percentual lido (0–100, EPUB).
        group.MapPut("/{publicationId:int}/percentage", async (
            int publicationId, UpdatePercentageRequest req, ClaimsPrincipal principal,
            IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            if (req.ReadPercentage is < 0 or > 100)
                return Results.BadRequest(new ErrorResponse("readPercentage deve estar entre 0 e 100.", "VALIDATION_ERROR"));

            var result = await reading.SavePercentageAsync(userId.Value, publicationId, req.ReadPercentage, ct);
            return result switch
            {
                SaveLastPageResult.Saved => Results.NoContent(),
                SaveLastPageResult.NotEpub => Results.Json(
                    new ErrorResponse("A publicação não é EPUB; não há percentual a salvar.", "PUBLICATION_NOT_EPUB"),
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.NotFound(),
            };
        });

        // RF21: marcação manual de página (bookmark). EPUB e PDF (sem restrição).
        group.MapPut("/{publicationId:int}/bookmark", async (
            int publicationId, SaveBookmarkRequest req, ClaimsPrincipal principal,
            IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            // Posição/location do leitor; 0-based como o last_page_read (CHECK
            // shelf_bookmarked_page_check relaxado para >= 0 — ver migração da #26).
            if (req.BookmarkedPage < 0)
                return Results.BadRequest(new ErrorResponse("bookmarkedPage deve ser maior ou igual a 0.", "VALIDATION_ERROR"));

            var ok = await reading.SaveBookmarkAsync(userId.Value, publicationId, req.BookmarkedPage, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // RF21: devolve a página marcada manualmente (ou null).
        group.MapGet("/{publicationId:int}/bookmark", async (
            int publicationId, ClaimsPrincipal principal, IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var page = await reading.GetBookmarkAsync(userId.Value, publicationId, ct);
            return Results.Ok(new BookmarkDto(page));
        });

        // RF18: retomada de leitura — devolve a última página lida (ou null).
        group.MapGet("/{publicationId:int}/last-page", async (
            int publicationId, ClaimsPrincipal principal, IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var lastPage = await reading.GetLastPageAsync(userId.Value, publicationId, ct);
            return Results.Ok(new LastPageReadDto(lastPage));
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
