namespace LivreMente.Api.Security;

/// <summary>
/// Abstração (Strategy) para hashing de senha. Desacopla o algoritmo do resto
/// da aplicação — a implementação atual usa BCrypt, mas pode ser trocada sem
/// tocar nos serviços. Reusada também na verificação de senha (login).
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}
