using System;

namespace LivreMente.Api.Models;

/// <summary>
/// Refresh token persistido (RF02 — "manter-se conectado"). Guarda apenas o hash;
/// o valor em claro fica com o cliente. Tem expiração e pode ser revogado
/// (logout ou rotação na renovação).
/// </summary>
public partial class RefreshToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime CreateDate { get; set; }

    public virtual User User { get; set; } = null!;
}
