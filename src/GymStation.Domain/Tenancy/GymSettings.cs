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

    /// <summary>Tenant accent color; shades derived and WCAG-validated on save (both modes).</summary>
    public string AccentColorHex { get; set; } = "#C9503B";

    /// <summary>Dark (mat/gi) is the platform base; gyms may default to the paper-ledger light mode.</summary>
    public bool DefaultThemeDark { get; set; } = true;

    /// <summary>File-store path for the gym's logo (png/jpg/webp); null renders the crest block.</summary>
    public string? LogoPath { get; set; }

    /// <summary>File-store path for the public-page hero image; null renders the mat-texture gradient.</summary>
    public string? HeroPath { get; set; }
}
