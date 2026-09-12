using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class AppUser
{
    public int Id { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public DateOnly BirthDate { get; set; }

    public string Gender { get; set; } = null!;

    public DateTime RegistryDate { get; set; }

    public virtual ICollection<Annotation> Annotations { get; set; } = new List<Annotation>();

    public virtual ICollection<Highlight> Highlights { get; set; } = new List<Highlight>();

    public virtual ICollection<Shelf> Shelves { get; set; } = new List<Shelf>();

    public virtual ICollection<UserPreference> UserPreferences { get; set; } = new List<UserPreference>();

    public virtual ICollection<WordLookup> WordLookups { get; set; } = new List<WordLookup>();

    public virtual ICollection<Genre> Genres { get; set; } = new List<Genre>();
}
