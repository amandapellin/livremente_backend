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
    IJwtTokenService jwtTokenService,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly LivreMenteDbContext _db = db;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;
    private readonly IJwtTokenService _jwtTokenService = jwtTokenService;
    private readonly IEmailSender _emailSender = emailSender;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<AuthService> _logger = logger;

    // Hash fixo usado para equalizar o tempo de resposta quando o e-mail não
    // existe (anti-enumeração por timing). Calculado uma vez por processo.
    private static string? _timingHash;

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
        var (plainToken, tokenHash) = OpaqueTokens.Create();
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

        var hash = OpaqueTokens.Hash(token);
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

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email!.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Usuário inexistente ou senha incorreta → mesma resposta (não revela qual).
        if (user is null)
        {
            RunTimingEqualizingVerify(request.Password!); // gasta ~o mesmo tempo de um Verify real
            return new LoginResult(LoginError.InvalidCredentials);
        }

        if (!_passwordHasher.Verify(request.Password!, user.PasswordHash))
            return new LoginResult(LoginError.InvalidCredentials);

        // Conta válida, mas ainda não confirmada (RN01).
        if (user.EmailConfirmedAt is null)
            return new LoginResult(LoginError.EmailNotConfirmed);

        var accessToken = _jwtTokenService.CreateAccessToken(user);
        var refreshPlain = IssueRefreshToken(user, request.RememberMe);
        user.LastLoginDate = Now();
        await _db.SaveChangesAsync(ct);

        var response = new LoginResponse(accessToken, refreshPlain, ToAuthUser(user));
        return new LoginResult(LoginError.None, response);
    }

    public async Task<RefreshResult> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return new RefreshResult(RefreshError.Invalid);

        var hash = OpaqueTokens.Hash(request.RefreshToken);
        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= Now())
            return new RefreshResult(RefreshError.Invalid);

        // Rotação: revoga o token usado e emite um novo, mantendo a validade
        // original (a sessão tem tempo máximo — a renovação não a estende para sempre).
        stored.RevokedAt = Now();
        var (plain, newHash) = OpaqueTokens.Create();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = stored.UserId,
            TokenHash = newHash,
            ExpiresAt = stored.ExpiresAt,
        });

        var accessToken = _jwtTokenService.CreateAccessToken(stored.User);
        await _db.SaveChangesAsync(ct);

        return new RefreshResult(RefreshError.None, new RefreshResponse(accessToken, plain));
    }

    /// <summary>Cria e anexa um refresh token ao usuário; retorna o valor em claro.</summary>
    private string IssueRefreshToken(User user, bool rememberMe)
    {
        var jwt = _configuration.GetSection("Jwt");
        var days = rememberMe
            ? (int.TryParse(jwt["RememberMeDays"], out var r) ? r : 30)
            : (int.TryParse(jwt["RefreshTokenDays"], out var d) ? d : 1);

        var (plain, hash) = OpaqueTokens.Create();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = Now().AddDays(days),
        });
        return plain;
    }

    private static AuthUserDto ToAuthUser(User user) =>
        new(user.Id.ToString(), user.FullName, user.Email);

    private void RunTimingEqualizingVerify(string password)
    {
        _timingHash ??= _passwordHasher.Hash("timing-equalizer");
        _passwordHasher.Verify(password, _timingHash);
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
