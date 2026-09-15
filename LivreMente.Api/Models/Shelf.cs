using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class Shelf
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int PublicationId { get; set; }

    public int? ReadPercentage { get; set; }

    public int? BookmarkedPage { get; set; }

    public int? LastPageRead { get; set; }

    public virtual Publication Publication { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
