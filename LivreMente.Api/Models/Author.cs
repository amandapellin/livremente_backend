using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class Author
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int? BirthYear { get; set; }

    public int? DeathYear { get; set; }

    public virtual ICollection<Material> Materials { get; set; } = new List<Material>();
}
