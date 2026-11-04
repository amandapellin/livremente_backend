using LivreMente.Api.Dtos;
using LivreMente.Api.Services;
using LivreMente.Api.Validation;

namespace LivreMente.Api.Endpoints;

/// <summary>
/// Contato do Suporte (#84). Público; handler fino: honeypot → validação → envio.
/// Protegido por rate limit (política "support-contact" no Program.cs).
/// </summary>
public static class SupportEndpoints
{
    public static RouteGroupBuilder MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/support").WithTags("Support");

        group.MapPost("/contact", async (ContactRequest req, ISupportService support, ILoggerFactory lf, CancellationToken ct) =>
        {
            // Honeypot: campo oculto no front. Se preenchido, é bot → aceita em
            // silêncio (sem enviar e sem revelar a armadilha).
            if (!string.IsNullOrWhiteSpace(req.Website))
                return Results.Accepted();

            var error = SupportValidation.Validate(req);
            if (error is not null)
                return Results.BadRequest(new ErrorResponse(error, "VALIDATION_ERROR"));

            try
            {
                await support.SendContactAsync(req, ct);
                return Results.Accepted();
            }
            catch (Exception ex)
            {
                lf.CreateLogger("Support").LogError(ex, "Falha ao enviar mensagem de contato do suporte.");
                return Results.Json(
                    new ErrorResponse("Não foi possível enviar sua mensagem. Tente novamente mais tarde.", "SUPPORT_EMAIL_FAILED"),
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequireRateLimiting("support-contact")
        .Produces(StatusCodes.Status202Accepted)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status429TooManyRequests)
        .WithSummary("Envia uma mensagem de contato do Suporte por e-mail.")
        .WithDescription("Público, com rate limit por IP e honeypot. 202 em sucesso, 400 em validação, 429 em excesso.");

        return group;
    }
}
