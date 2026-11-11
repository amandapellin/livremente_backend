using Azure;
using Azure.Communication.Email;

namespace LivreMente.Api.Email;

/// <summary>
/// Implementação de <see cref="IEmailSender"/> via <b>Azure Communication Services (Email)</b>.
/// Usa o SDK <c>Azure.Communication.Email</c> com a connection string do recurso ACS
/// (seção de configuração <c>Email:Acs</c>) — entrega real de e-mail <b>sem SMTP</b> e sem
/// registro de aplicativo no Entra ID. Em produção, a connection string vem de variável de
/// ambiente / Key Vault, nunca do <c>appsettings</c> versionado.
/// </summary>
public class AcsEmailSender(IConfiguration configuration) : IEmailSender
{
    private readonly IConfiguration _configuration = configuration;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var acs = _configuration.GetSection("Email:Acs");
        var connectionString = acs["ConnectionString"]
            ?? throw new InvalidOperationException("Email:Acs:ConnectionString não configurado.");
        var fromAddress = acs["FromAddress"]
            ?? throw new InvalidOperationException("Email:Acs:FromAddress não configurado.");

        var client = new EmailClient(connectionString);

        // WaitUntil.Started: enfileira o envio e retorna assim que o ACS aceita a requisição
        // (erros de credencial/remetente surgem aqui); não bloqueia a resposta esperando a
        // entrega final. O remetente é o endereço MailFrom do domínio azurecomm conectado.
        await client.SendAsync(
            WaitUntil.Started,
            senderAddress: fromAddress,
            recipientAddress: to,
            subject: subject,
            htmlContent: htmlBody,
            cancellationToken: ct);
    }
}
