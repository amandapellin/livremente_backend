namespace LivreMente.Api.Email;

/// <summary>
/// Implementação de desenvolvimento de <see cref="IEmailSender"/>: não envia
/// e-mail de verdade — registra o conteúdo (incluindo o link de confirmação) no
/// log, permitindo testar o fluxo sem um provedor SMTP.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger = logger;

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        _logger.LogInformation("[E-mail DEV] Para: {To} | Assunto: {Subject}\n{Body}", to, subject, htmlBody);
        return Task.CompletedTask;
    }
}
