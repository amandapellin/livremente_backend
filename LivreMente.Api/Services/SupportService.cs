using System.Net;
using LivreMente.Api.Dtos;
using LivreMente.Api.Email;

namespace LivreMente.Api.Services;

/// <summary>
/// Envia a mensagem de contato do Suporte (#84) por e-mail, reutilizando o
/// <see cref="IEmailSender"/> (Logging em dev, SMTP em prod — mesma config do cadastro).
/// Destino em <c>Support:Email</c> (cai no remetente do SMTP se ausente).
/// </summary>
public class SupportService(IEmailSender emailSender, IConfiguration config) : ISupportService
{
    private readonly IEmailSender _emailSender = emailSender;
    private readonly IConfiguration _config = config;

    public async Task SendContactAsync(ContactRequest req, CancellationToken ct = default)
    {
        var to = _config["Support:Email"]
            ?? _config["Email:Smtp:FromAddress"]
            ?? throw new InvalidOperationException("Support:Email não configurado.");

        // Escapa a entrada do usuário para não injetar HTML no corpo do e-mail.
        var name = WebUtility.HtmlEncode(req.Name!.Trim());
        var email = WebUtility.HtmlEncode(req.Email!.Trim());
        var subject = req.Subject!.Trim();
        var subjectHtml = WebUtility.HtmlEncode(subject);
        var message = WebUtility.HtmlEncode(req.Message!.Trim()).Replace("\n", "<br>");

        var body = $"""
            <p><strong>Nova mensagem de suporte</strong></p>
            <p><strong>Nome:</strong> {name}<br>
            <strong>E-mail:</strong> {email}<br>
            <strong>Assunto:</strong> {subjectHtml}</p>
            <hr>
            <p>{message}</p>
            """;

        await _emailSender.SendAsync(to, $"[Suporte LivreMente] {subject}", body, ct);
    }
}
