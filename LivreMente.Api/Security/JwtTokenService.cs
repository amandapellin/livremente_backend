using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using LivreMente.Api.Models;

namespace LivreMente.Api.Security;

/// <summary>
/// Implementação de <see cref="IJwtTokenService"/> com JWT assinado em HS256.
/// A chave e os metadados (issuer/audience/expiração) vêm da seção `Jwt` da
/// configuração; a chave (`Jwt:Key`) é segredo e deve ter ≥ 32 bytes (256 bits).
/// </summary>
public class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    private readonly IConfiguration _configuration = configuration;

    public string CreateAccessToken(User user)
    {
        var jwt = _configuration.GetSection("Jwt");
        var key = jwt["Key"] ?? throw new InvalidOperationException("Jwt:Key não configurado.");
        var minutes = int.TryParse(jwt["AccessTokenMinutes"], out var m) ? m : 30;

        // Claims: identidade mínima do usuário. `sub` é o padrão para o id do sujeito.
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("name", user.FullName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
