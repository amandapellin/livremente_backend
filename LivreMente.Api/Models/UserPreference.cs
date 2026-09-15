using System;
using System.Collections.Generic;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Models;

public partial class UserPreference
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public PreferenceType PreferenceType { get; set; }

    public string PreferenceValue { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
