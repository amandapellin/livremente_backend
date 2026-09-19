using System;

namespace LivreMente.Api.Models;

/// <summary>
/// Token de confirmação de e-mail (RN01). Guarda apenas o hash do token; o valor
/// em claro só existe no link enviado por e-mail. Uso único e com expiração.
/// </summary>
public partial class EmailConfirmation
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public DateTime CreateDate { get; set; }

    public virtual User User { get; set; } = null!;
}
