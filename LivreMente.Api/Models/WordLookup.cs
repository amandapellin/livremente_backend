using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class WordLookup
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int PublicationId { get; set; }

    public string Word { get; set; } = null!;

    /// <summary>Definição consultada (1ª acepção), para o histórico (#27). Pode ser nula.</summary>
    public string? Definition { get; set; }

    public DateTime ConsultDate { get; set; }

    public virtual Publication Publication { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
