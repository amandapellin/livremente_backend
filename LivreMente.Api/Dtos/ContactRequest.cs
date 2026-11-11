namespace LivreMente.Api.Dtos;

/// <summary>
/// Mensagem do formulário de contato do Suporte (#84). <c>Website</c> é um campo
/// <b>honeypot</b>: fica oculto no front e deve chegar vazio; preenchido = provável bot.
/// </summary>
public record ContactRequest(string? Name, string? Email, string? Subject, string? Message, string? Website);
