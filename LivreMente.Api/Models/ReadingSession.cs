using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class ReadingSession
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int PublicationId { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public virtual Publication Publication { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
