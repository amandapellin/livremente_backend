using System.Security.Claims;
using LivreMente.Api.Dtos;
using LivreMente.Api.Security;
using LivreMente.Api.Services;
using LivreMente.Api.Validation;

namespace LivreMente.Api.Endpoints;

/// <summary>
/// Endpoints do perfil do usuário autenticado (RF03). Todos exigem JWT; a
/// identidade vem do token (`/me`), atendendo a RN04 sem parâmetro de id.
/// </summary>
public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/me", async (ClaimsPrincipal principal, IUserService users, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var profile = await users.GetProfileAsync(userId.Value, ct);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });

        group.MapPut("/me", async (UpdateProfileRequest req, ClaimsPrincipal principal, IUserService users, CancellationToken ct) =>
        {
            var validationError = ProfileValidation.ValidateUpdate(req);
            if (validationError is not null)
                return Results.BadRequest(new ErrorResponse(validationError, "VALIDATION_ERROR"));

            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var profile = await users.UpdateProfileAsync(userId.Value, req.Name!, ct);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });

        group.MapPatch("/me/password", async (ChangePasswordRequest req, ClaimsPrincipal principal, IUserService users, CancellationToken ct) =>
        {
            var validationError = ProfileValidation.ValidatePasswordChange(req);
            if (validationError is not null)
                return Results.BadRequest(new ErrorResponse(validationError, "VALIDATION_ERROR"));

            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await users.ChangePasswordAsync(userId.Value, req.CurrentPassword!, req.NewPassword!, ct);
            return result switch
            {
                ChangePasswordError.None => Results.NoContent(),
                ChangePasswordError.InvalidCurrentPassword => Results.Json(
                    new ErrorResponse("Senha atual incorreta.", "INVALID_CURRENT_PASSWORD"),
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.NotFound(),
            };
        });

        // Upload do avatar (multipart/form-data, campo "file"). Autenticado.
        group.MapPut("/me/avatar", async (IFormFile file, ClaimsPrincipal principal, IUserService users, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
                return Results.Unauthorized();

            if (file is null || file.Length == 0)
                return Unprocessable("Envie um arquivo de imagem.");
            if (file.Length > AvatarValidation.MaxBytes)
                return Unprocessable("A imagem deve ter no máximo 2 MB.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            var contentType = AvatarValidation.DetectImageType(bytes);
            if (contentType is null)
                return Unprocessable("Formato inválido. Envie uma imagem PNG ou JPG.");

            var result = await users.SetAvatarAsync(userId.Value, bytes, contentType, ct);
            return result == AvatarError.None ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery();

        // Exibição do avatar. Público (a tag <img> não envia o token); avatar é dado
        // de baixa sensibilidade. Sobrescreve o RequireAuthorization do grupo.
        group.MapGet("/{id:int}/avatar", async (int id, IUserService users, CancellationToken ct) =>
        {
            var avatar = await users.GetAvatarAsync(id, ct);
            return avatar is null
                ? Results.NotFound()
                : Results.File(avatar.Value.Content, avatar.Value.ContentType);
        }).AllowAnonymous();

        return group;
    }

    private static IResult Unprocessable(string message) =>
        Results.Json(new ErrorResponse(message, "VALIDATION_ERROR"), statusCode: StatusCodes.Status422UnprocessableEntity);
}
