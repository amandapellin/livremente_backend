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

        return group;
    }
}
