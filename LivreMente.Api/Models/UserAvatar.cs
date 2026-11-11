using System;

namespace LivreMente.Api.Models;

/// <summary>
/// Imagem de avatar do usuário (RF03), armazenada como bytes no banco. Relação
/// 1:1 com <see cref="User"/> — a chave primária é o próprio `user_id`. Fica em
/// tabela separada para não pesar as consultas ao `users`.
/// </summary>
public partial class UserAvatar
{
    public int UserId { get; set; }

    public byte[] Content { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public DateTime UpdateDate { get; set; }

    public virtual User User { get; set; } = null!;
}
