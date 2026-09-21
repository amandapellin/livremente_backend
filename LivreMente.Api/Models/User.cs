using System;
using System.Collections.Generic;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Models;

public partial class User
{
    public int Id { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public DateOnly BirthDate { get; set; }

    public Gender Gender { get; set; }

    public DateTime CreateDate { get; set; }

    public DateTime UpdateDate { get; set; }

    public DateTime? LastLoginDate { get; set; }

    public DateTime? EmailConfirmedAt { get; set; }

    public DateTime? LgpdConsentedAt { get; set; }

    public virtual ICollection<Annotation> Annotations { get; set; } = [];

    public virtual ICollection<EmailConfirmation> EmailConfirmations { get; set; } = [];

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    public virtual ICollection<Highlight> Highlights { get; set; } = [];

    public virtual ICollection<ReadingSession> ReadingSessions { get; set; } = [];

    public virtual ICollection<Shelf> Shelves { get; set; } = [];

    public virtual ICollection<UserPreference> UserPreferences { get; set; } = [];

    public virtual ICollection<WordLookup> WordLookups { get; set; } = [];

    public virtual ICollection<Genre> Genres { get; set; } = [];
}
