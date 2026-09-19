namespace LivreMente.Api.Dtos;

/// <summary>
/// Corpo de erro padronizado (400/409). <c>Message</c> é amigável para exibição;
/// <c>Code</c> é um código estável para o front tratar (ex.: EMAIL_ALREADY_EXISTS).
/// </summary>
public record ErrorResponse(string Message, string? Code = null);
