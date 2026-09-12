using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class Genre
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<Material> Materials { get; set; } = new List<Material>();

    public virtual ICollection<AppUser> Users { get; set; } = new List<AppUser>();
}
