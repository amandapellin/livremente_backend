using System.Security.Cryptography;
using System.Text;

namespace LivreMente.Api.Security;

/// <summary>
/// Geração e hashing de tokens de confirmação de e-mail. O token é aleatório de
/// alta entropia (256 bits), então um hash rápido (SHA-256) é adequado — ao
/// contrário de senhas, não precisa de BCrypt. Só o hash é persistido; o valor
/// em claro viaja apenas no link do e-mail.
/// </summary>
public static class ConfirmationTokens
{
    /// <summary>Gera um novo token: retorna o valor em claro (para o link) e o hash (para persistir).</summary>
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
