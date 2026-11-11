namespace LivreMente.Api.Services;

/// <summary>Contato do Suporte (#84): envia a mensagem por e-mail ao destino configurado.</summary>
public interface ISupportService
{
    Task SendContactAsync(Dtos.ContactRequest req, CancellationToken ct = default);
}
