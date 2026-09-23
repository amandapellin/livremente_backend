using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace LivreMente.Api.Security;

/// <summary>Acesso à identidade do usuário autenticado a partir do JWT.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Id do usuário, lido do claim `sub` do token (fallback: NameIdentifier).</summary>
    public static int? GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}
