using LivreMente.Api.Models;

namespace LivreMente.Api.Security;

/// <summary>
/// Emissão do token de acesso (JWT). Abstração (Strategy) que isola a geração do
/// token do resto da aplicação, deixando o <c>AuthService</c> livre desse detalhe.
/// </summary>
public interface IJwtTokenService
{
    string CreateAccessToken(User user);
}
