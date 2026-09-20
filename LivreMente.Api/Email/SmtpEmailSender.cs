using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LivreMente.Api.Email;

/// <summary>
/// Implementação real de <see cref="IEmailSender"/> via SMTP (MailKit).
/// As credenciais vêm da seção de configuração <c>Email:Smtp</c> — em produção,
/// use variáveis de ambiente / user-secrets, nunca o appsettings versionado.
/// </summary>
public class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    private readonly IConfiguration _configuration = configuration;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var smtp = _configuration.GetSection("Email:Smtp");
        var host = smtp["Host"] ?? throw new InvalidOperationException("Email:Smtp:Host não configurado.");
        var port = int.TryParse(smtp["Port"], out var p) ? p : 587;
        var username = smtp["Username"];
        var password = smtp["Password"];
        var fromAddress = smtp["FromAddress"] ?? username ?? throw new InvalidOperationException("Email:Smtp:FromAddress não configurado.");
        var fromName = smtp["FromName"] ?? "LivreMente";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, SecureSocketOptions.StartTlsWhenAvailable, ct);
        if (!string.IsNullOrEmpty(username))
            await client.AuthenticateAsync(username, password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
