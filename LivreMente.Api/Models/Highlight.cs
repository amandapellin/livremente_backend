using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class Highlight
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int PublicationId { get; set; }

    public string Excerpt { get; set; } = null!;

    public string EpubPosition { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Publication Publication { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
