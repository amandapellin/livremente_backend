using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using LivreMente.Api.Dtos;
using LivreMente.Api.Email;
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
public class AuthService(
    LivreMenteDbContext db,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly LivreMenteDbContext _db = db;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;
    private readonly IEmailSender _emailSender = emailSender;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<AuthService> _logger = logger;

    /// <summary>Timestamp UTC como Unspecified — exigido pelas colunas `timestamp without time zone`.</summary>
    private static DateTime Now() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

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
            LgpdConsentedAt = Now(),
        };

        await ApplyPreferencesAsync(user, request.Preferences, ct);

        // Token de confirmação (RN01): usuário nasce não confirmado (EmailConfirmedAt = null).
        var ttlHours = _configuration.GetValue<int?>("App:EmailConfirmationTtlHours") ?? 24;
        var (plainToken, tokenHash) = ConfirmationTokens.Create();
        user.EmailConfirmations.Add(new EmailConfirmation
        {
            TokenHash = tokenHash,
            ExpiresAt = Now().AddHours(ttlHours),
        });

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

        // Envio após o commit: falha de e-mail não desfaz o cadastro (permite reenvio futuro).
        await SendConfirmationEmailAsync(user.Email, plainToken, ct);

        return new RegisterResult(RegisterError.None, user.Id);
    }

    public async Task<ConfirmResult> ConfirmAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return ConfirmResult.InvalidOrExpired;

        var hash = ConfirmationTokens.Hash(token);
        var confirmation = await _db.EmailConfirmations
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.TokenHash == hash, ct);

        if (confirmation is null || confirmation.ConfirmedAt is not null || confirmation.ExpiresAt <= Now())
            return ConfirmResult.InvalidOrExpired;

        var now = Now();
        confirmation.ConfirmedAt = now;
        confirmation.User.EmailConfirmedAt = now;
        await _db.SaveChangesAsync(ct);

        return ConfirmResult.Confirmed;
    }

    private async Task SendConfirmationEmailAsync(string email, string plainToken, CancellationToken ct)
    {
        var apiBaseUrl = (_configuration["App:PublicApiBaseUrl"] ?? "http://localhost:5091").TrimEnd('/');
        var link = $"{apiBaseUrl}/api/auth/confirm?token={plainToken}";
        var body = $"""
            <p>Bem-vindo(a) ao LivreMente!</p>
            <p>Confirme seu cadastro clicando no link abaixo:</p>
            <p><a href="{link}">Confirmar minha conta</a></p>
            <p>Se você não criou esta conta, ignore este e-mail.</p>
            """;

        try
        {
            await _emailSender.SendAsync(email, "Confirme seu cadastro no LivreMente", body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar e-mail de confirmação para {Email}.", email);
        }
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
