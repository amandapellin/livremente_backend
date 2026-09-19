namespace LivreMente.Api.Email;

/// <summary>
/// Abstração (Strategy) de envio de e-mail. A implementação de dev registra em
/// log; uma implementação SMTP real pode ser plugada por configuração, sem
/// tocar nos serviços que enviam e-mails.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}
