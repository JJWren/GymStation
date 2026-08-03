namespace GymStation.Domain.Tenancy;

/// <summary>Who finalizes an accepted substitution (per-gym choice, Q11).</summary>
public enum SubstitutionMode
{
    /// <summary>Accepted/claimed substitutions apply immediately; admins are notified.</summary>
    AutoApply = 0,

    /// <summary>Accepted substitutions wait for an admin approval before applying.</summary>
    AdminGate = 1,
}

/// <summary>First-class per-tenant configuration. One row per Gym.</summary>
public class GymSettings : ITenantOwned
{
    public Guid GymId { get; set; }

    public SubstitutionMode SubstitutionMode { get; set; } = SubstitutionMode.AutoApply;

    /// <summary>Allow "anyone cover Tuesday?" requests any instructor can claim.</summary>
    public bool OpenClaimsEnabled { get; set; } = true;

    /// <summary>Self check-in opens this many minutes before session start (closes at session end).</summary>
    public int CheckInWindowMinutes { get; set; } = 60;

    /// <summary>Mat hours: the schedule's time rail spans exactly this window.</summary>
    public TimeOnly OpenTime { get; set; } = new(6, 0);

    /// <summary>Mat hours end; must sit after OpenTime.</summary>
    public TimeOnly CloseTime { get; set; } = new(22, 0);

    /// <summary>Tenant accent color; shades derived and WCAG-validated on save (both modes).</summary>
    public string AccentColorHex { get; set; } = "#C9503B";

    /// <summary>Dark (mat/gi) is the platform base; gyms may default to the paper-ledger light mode.</summary>
    public bool DefaultThemeDark { get; set; } = true;

    /// <summary>File-store path for the gym's logo (png/jpg/webp); null renders the crest block.</summary>
    public string? LogoPath { get; set; }

    /// <summary>File-store path for the public-page hero image; null renders the mat-texture gradient.</summary>
    public string? HeroPath { get; set; }

    // ---- Landing content (#93): the hero strip labels are REAL anchors to the
    // page's sections, and VISIT is how a walk-in actually finds the gym. ----

    /// <summary>Strip label anchoring to #schedule.</summary>
    public string TaglineSchedule { get; set; } = "SCHEDULE";

    /// <summary>Strip label anchoring to #instructors.</summary>
    public string TaglineInstructors { get; set; } = "INSTRUCTORS";

    /// <summary>Strip label anchoring to #visit.</summary>
    public string TaglineVisit { get; set; } = "VISIT";

    public string? VisitAddress { get; set; }
    public string? VisitPhone { get; set; }
    public string? VisitEmail { get; set; }
    public string? SocialInstagram { get; set; }
    public string? SocialFacebook { get; set; }
    public string? SocialYouTube { get; set; }
    public string? SocialTikTok { get; set; }
    public string? SocialX { get; set; }
}
