namespace LivreMente.Api.Dtos;

/// <summary>
/// Corpo do `PUT /api/shelf/{publicationId}` (RF06, #33). `Status` chega como
/// string e é validado contra os quatro valores do enum no endpoint
/// (read, reading, want_to_read, abandoned) — valor inválido → 400.
/// </summary>
public record UpdateShelfStatusRequest(string Status);
