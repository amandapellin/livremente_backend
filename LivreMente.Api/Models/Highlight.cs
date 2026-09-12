using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class Highlight
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int MaterialId { get; set; }

    public string Excerpt { get; set; } = null!;

    public string EpubPosition { get; set; } = null!;

    public DateTime RegistryDate { get; set; }

    public virtual Material Material { get; set; } = null!;

    public virtual AppUser User { get; set; } = null!;
}
