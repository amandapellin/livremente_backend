using LivreMente.Api.Dtos;
using LivreMente.Api.Services;
using LivreMente.Api.Validation;

namespace LivreMente.Api.Endpoints;

/// <summary>
/// Endpoints de autenticação. Handlers finos: validam a entrada, delegam ao
/// serviço e mapeiam o resultado para os códigos HTTP do contrato.
/// </summary>
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest req, IAuthService auth, CancellationToken ct) =>
        {
            var validationError = RegisterValidation.Validate(req);
            if (validationError is not null)
                return Results.BadRequest(new ErrorResponse(validationError, "VALIDATION_ERROR"));

            var result = await auth.RegisterAsync(req, ct);
            if (result.Error == RegisterError.EmailAlreadyExists)
                return Results.Conflict(new ErrorResponse("Este e-mail já está cadastrado.", "EMAIL_ALREADY_EXISTS"));

            var response = new RegisterResponse(result.UserId!.Value.ToString(), req.Name!.Trim(), req.Email!.Trim());
            return Results.Created($"/api/users/{result.UserId}", response);
        });

        // Clicado a partir do link no e-mail (navegação do browser): confirma e
        // redireciona para o /login do front com o resultado na query.
        group.MapGet("/confirm", async (string? token, IAuthService auth, IConfiguration config, CancellationToken ct) =>
        {
            var result = await auth.ConfirmAsync(token ?? "", ct);
            var frontendBaseUrl = (config["App:FrontendBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
            var status = result == ConfirmResult.Confirmed ? "1" : "invalid";
            return Results.Redirect($"{frontendBaseUrl}/login?confirmed={status}");
        });

        group.MapPost("/login", async (LoginRequest req, IAuthService auth, CancellationToken ct) =>
        {
            var validationError = LoginValidation.Validate(req);
            if (validationError is not null)
                return Results.BadRequest(new ErrorResponse(validationError, "VALIDATION_ERROR"));

            var result = await auth.LoginAsync(req, ct);
            return result.Error switch
            {
                LoginError.None => Results.Ok(result.Response),
                LoginError.EmailNotConfirmed => Results.Json(
                    new ErrorResponse("Confirme seu e-mail antes de entrar.", "EMAIL_NOT_CONFIRMED"),
                    statusCode: StatusCodes.Status403Forbidden),
                // InvalidCredentials (e qualquer outro): mensagem genérica, sem dizer qual campo errou.
                _ => Results.Json(
                    new ErrorResponse("E-mail ou senha inválidos.", "INVALID_CREDENTIALS"),
                    statusCode: StatusCodes.Status401Unauthorized),
            };
        });

        group.MapPost("/refresh", async (RefreshRequest req, IAuthService auth, CancellationToken ct) =>
        {
            var result = await auth.RefreshAsync(req, ct);
            return result.Error == RefreshError.None
                ? Results.Ok(result.Response)
                : Results.Json(
                    new ErrorResponse("Sessão inválida. Faça login novamente.", "INVALID_REFRESH_TOKEN"),
                    statusCode: StatusCodes.Status401Unauthorized);
        });

        return group;
    }
}
