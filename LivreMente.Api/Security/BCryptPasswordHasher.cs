namespace LivreMente.Api.Security;

/// <summary>
/// Implementação de <see cref="IPasswordHasher"/> com BCrypt. O hash resultante
/// tem 60 caracteres, cabendo na coluna <c>password_hash</c> (<c>varchar(70)</c>).
/// </summary>
public class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}
