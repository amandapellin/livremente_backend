using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class Shelf
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int MaterialId { get; set; }

    public string Status { get; set; } = null!;

    public decimal? ReadPercentage { get; set; }

    public int? CurrentSessionTime { get; set; }

    public int? BookmarkedPage { get; set; }

    public int? LastPageRead { get; set; }

    public virtual Material Material { get; set; } = null!;

    public virtual AppUser User { get; set; } = null!;
}
