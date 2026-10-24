using System.Security.Claims;
using LivreMente.Api.Dtos;
using LivreMente.Api.Security;
using LivreMente.Api.Services;
using LivreMente.Api.Validation;

namespace LivreMente.Api.Endpoints;

/// <summary>
/// Endpoints do épico de Leitura (autenticados; operam sobre a estante do
/// próprio usuário, identidade do JWT — RN04 por construção).
/// </summary>
public static class ReadingEndpoints
{
    // Cores de grifo aceitas (slugs do front; casam com o CHECK highlight_color_check).
    private static readonly HashSet<string> AllowedHighlightColors = new(StringComparer.Ordinal)
    {
        "amarelo", "verde", "azul", "rosa",
    };

    public static RouteGroupBuilder MapReadingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reading").WithTags("Reading").RequireAuthorization();

        // RF09 (#35): calendário de frequência de leitura do usuário. Recurso de
        // usuário (não por publicação); `from`/`to` (yyyy-MM-dd) são opcionais —
        // sem ambos, usa o mês atual. A rota literal "/calendar" não colide com
        // "/{publicationId:int}/..." (constraint int). Identidade do JWT (RN04).
        group.MapGet("/calendar", async (
            ClaimsPrincipal principal, IReadingService reading, CancellationToken ct,
            string? from = null, string? to = null) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var (error, rangeFrom, rangeTo) = ReadingCalendarValidation.Resolve(from, to);
            if (error is not null)
                return Results.BadRequest(new ErrorResponse(error, "VALIDATION_ERROR"));

            var calendar = await reading.GetCalendarAsync(userId.Value, rangeFrom, rangeTo, ct);
            return Results.Ok(calendar);
        })
        .Produces<ReadingCalendarDto>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // RF27 (#37): métricas agregadas de leitura do usuário (cards de estatística
        // da tela de recomendações). Identidade do JWT (RN04); sem parâmetros.
        group.MapGet("/stats", async (
            ClaimsPrincipal principal, IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            return Results.Ok(await reading.GetStatsAsync(userId.Value, ct));
        })
        .WithSummary("Métricas agregadas de leitura do usuário (tempo, sequência, dias).")
        .Produces<ReadingStatsDto>(StatusCodes.Status200OK);

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

        // RF22/RN07: cria um grifo (só EPUB).
        group.MapPost("/{publicationId:int}/highlights", async (
            int publicationId, CreateHighlightRequest req, ClaimsPrincipal principal,
            IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(req.Excerpt))
                return Results.BadRequest(new ErrorResponse("excerpt é obrigatório.", "VALIDATION_ERROR"));
            if (string.IsNullOrWhiteSpace(req.EpubPosition) || req.EpubPosition.Length > 255)
                return Results.BadRequest(new ErrorResponse("epubPosition é obrigatório e deve ter no máximo 255 caracteres.", "VALIDATION_ERROR"));
            if (!AllowedHighlightColors.Contains(req.Color))
                return Results.BadRequest(new ErrorResponse("color inválida (use amarelo, verde, azul ou rosa).", "VALIDATION_ERROR"));

            var result = await reading.CreateHighlightAsync(userId.Value, publicationId, req, ct);
            return result.Status switch
            {
                SaveLastPageResult.Saved => Results.Json(result.Highlight, statusCode: StatusCodes.Status201Created),
                SaveLastPageResult.NotEpub => Results.Json(
                    new ErrorResponse("A publicação não é EPUB; grifos são permitidos apenas em EPUB (RN07).", "PUBLICATION_NOT_EPUB"),
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.NotFound(),
            };
        });

        // RF22: lista os grifos do usuário para a publicação.
        group.MapGet("/{publicationId:int}/highlights", async (
            int publicationId, ClaimsPrincipal principal, IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var highlights = await reading.GetHighlightsAsync(userId.Value, publicationId, ct);
            return Results.Ok(highlights);
        });

        // RF23/RN07: cria uma anotação vinculada a um trecho (só EPUB).
        group.MapPost("/{publicationId:int}/annotations", async (
            int publicationId, CreateAnnotationRequest req, ClaimsPrincipal principal,
            IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(req.LinkedExcerpt))
                return Results.BadRequest(new ErrorResponse("linkedExcerpt é obrigatório.", "VALIDATION_ERROR"));
            if (string.IsNullOrWhiteSpace(req.Content))
                return Results.BadRequest(new ErrorResponse("content é obrigatório.", "VALIDATION_ERROR"));
            if (string.IsNullOrWhiteSpace(req.EpubPosition) || req.EpubPosition.Length > 255)
                return Results.BadRequest(new ErrorResponse("epubPosition é obrigatório e deve ter no máximo 255 caracteres.", "VALIDATION_ERROR"));

            var result = await reading.CreateAnnotationAsync(userId.Value, publicationId, req, ct);
            return result.Status switch
            {
                SaveLastPageResult.Saved => Results.Json(result.Annotation, statusCode: StatusCodes.Status201Created),
                SaveLastPageResult.NotEpub => Results.Json(
                    new ErrorResponse("A publicação não é EPUB; anotações são permitidas apenas em EPUB (RN07).", "PUBLICATION_NOT_EPUB"),
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.NotFound(),
            };
        });

        // RF23: lista as anotações do usuário para a publicação.
        group.MapGet("/{publicationId:int}/annotations", async (
            int publicationId, ClaimsPrincipal principal, IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var annotations = await reading.GetAnnotationsAsync(userId.Value, publicationId, ct);
            return Results.Ok(annotations);
        });

        // RF24: ao reabrir a obra, grifos + anotações num único snapshot (listas
        // vazias quando não há nada; nunca erro).
        group.MapGet("/{publicationId:int}/highlights-annotations", async (
            int publicationId, ClaimsPrincipal principal, IReadingService reading, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var notes = await reading.GetHighlightsAndAnnotationsAsync(userId.Value, publicationId, ct);
            return Results.Ok(notes);
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

        // RF15/RF16: proxy dos arquivos de leitura. **Exige autenticação** (consultar
        // o acervo é público, mas LER exige login) — herda o RequireAuthorization do
        // grupo. O front busca o arquivo via fetch com o JWT (ArrayBuffer p/ o epub.js;
        // Blob URL p/ o iframe do PDF), já que epub.js/iframe não enviam o header.
        // O servidor busca na fonte externa (sem CORS) e transmite pela mesma origem.
        group.MapGet("/{publicationId:int}/epub",
            (int publicationId, IReadingService reading, CancellationToken ct) =>
                ServeFileAsync(reading, publicationId, PublicationFileKind.Epub, ct));

        group.MapGet("/{publicationId:int}/pdf",
            (int publicationId, IReadingService reading, CancellationToken ct) =>
                ServeFileAsync(reading, publicationId, PublicationFileKind.Pdf, ct));

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
