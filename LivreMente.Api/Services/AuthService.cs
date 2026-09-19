using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using LivreMente.Api.Dtos;
using LivreMente.Api.Mappings;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;
using LivreMente.Api.Security;

namespace LivreMente.Api.Services;

/// <summary>
/// Regra de negócio de autenticação/cadastro. Cria o usuário e aplica as
/// preferências de leitura numa única transação (um só SaveChanges).
/// Pressupõe que o payload já passou por <c>RegisterValidation</c>.
/// </summary>
public class AuthService(LivreMenteDbContext db, IPasswordHasher passwordHasher) : IAuthService
{
    private readonly LivreMenteDbContext _db = db;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email!.Trim();

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return new RegisterResult(RegisterError.EmailAlreadyExists);

        var user = new User
        {
            FullName = request.Name!.Trim(),
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password!),
            BirthDate = DateOnly.Parse(request.BirthDate!, CultureInfo.InvariantCulture),
            Gender = Enum.Parse<Gender>(request.Gender!),
        };

        await ApplyPreferencesAsync(user, request.Preferences, ct);

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Corrida: outro cadastro com o mesmo e-mail entre o AnyAsync e o insert
            // viola o índice único users_email_key. Demais erros propagam (não são mascarados).
            return new RegisterResult(RegisterError.EmailAlreadyExists);
        }

        return new RegisterResult(RegisterError.None, user.Id);
    }

    /// <summary>
    /// Traduz as preferências do vocabulário do front para o do banco, via
    /// <see cref="PreferenceCatalog"/>, e as anexa ao usuário. Ponto de reuso
    /// futuro para a edição de preferências no perfil (issues #11/#12).
    /// </summary>
    private async Task ApplyPreferencesAsync(User user, RegisterPreferences? prefs, CancellationToken ct)
    {
        if (prefs is null)
            return;

        foreach (var lang in (prefs.Languages ?? []).Distinct())
            user.UserPreferences.Add(new UserPreference { PreferenceType = PreferenceType.language, PreferenceValue = lang });

        foreach (var publication in (prefs.Publications ?? []).Distinct())
            user.UserPreferences.Add(new UserPreference { PreferenceType = PreferenceType.content_type, PreferenceValue = publication });

        // categories (livro E/OU artigo) + literaryGenres → roteados por catálogo (slugs disjuntos).
        var slugs = (prefs.Categories ?? []).Concat(prefs.LiteraryGenres ?? []).Distinct();
        var genreNames = new HashSet<string>();
        var knowledgeAreas = new HashSet<string>();

        foreach (var slug in slugs)
        {
            if (PreferenceCatalog.BookGenres.TryGetValue(slug, out var names))
            {
                foreach (var name in names)
                    genreNames.Add(name);
            }
            else if (PreferenceCatalog.ArticleArchives.TryGetValue(slug, out var archives))
            {
                foreach (var archive in archives)
                    knowledgeAreas.Add(archive);
            }
        }

        foreach (var area in knowledgeAreas)
            user.UserPreferences.Add(new UserPreference { PreferenceType = PreferenceType.knowledge_area, PreferenceValue = area });

        if (genreNames.Count > 0)
        {
            var genres = await _db.Genres.Where(g => genreNames.Contains(g.Name)).ToListAsync(ct);
            foreach (var genre in genres)
                user.Genres.Add(genre);
        }
    }
}
