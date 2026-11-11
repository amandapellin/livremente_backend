using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class Annotation
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int PublicationId { get; set; }

    public string LinkedExcerpt { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string EpubPosition { get; set; } = null!;

    public DateTime CreateDate { get; set; }

    public virtual Publication Publication { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
