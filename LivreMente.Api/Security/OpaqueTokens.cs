using System.Security.Cryptography;
using System.Text;

namespace LivreMente.Api.Security;

/// <summary>
/// Geração e hashing de tokens opacos de alta entropia (256 bits), usados para
/// confirmação de e-mail e refresh token. Como são aleatórios, um hash rápido
/// (SHA-256) basta — ao contrário de senhas, não precisam de BCrypt. Só o hash
/// é persistido; o valor em claro viaja apenas para o cliente (link ou resposta).
/// </summary>
public static class OpaqueTokens
{
    /// <summary>Gera um token: valor em claro (para o cliente) e o hash (para persistir).</summary>
    public static (string Plain, string Hash) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plain = Base64UrlEncode(bytes);
        return (plain, Hash(plain));
    }

    /// <summary>Hash SHA-256 (hex minúsculo) do token, usado para busca/comparação.</summary>
    public static string Hash(string plain)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(plain));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
