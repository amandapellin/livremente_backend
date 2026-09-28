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
        var safeSubject = (subject ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        _logger.LogInformation(
            "[E-mail DEV] E-mail capturado em ambiente de desenvolvimento. Assunto: {Subject}",
            safeSubject
        );
        return Task.CompletedTask;
    }
}
