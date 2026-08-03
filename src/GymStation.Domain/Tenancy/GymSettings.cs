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

    // ---- Landing sections (#134): the public page's orderable marketing
    // sections. Empty content auto-hides a section; ordering is admin-set. ----

    /// <summary>Comma list of LandingSections keys; normalized on read, so stored
    /// junk can only ever degrade to the default funnel.</summary>
    public string SectionOrder { get; set; } = string.Join(',', LandingSections.Default);

    /// <summary>Heading for the About section's strip anchor and header.</summary>
    public string AboutTitle { get; set; } = "ABOUT";

    /// <summary>The gym's own story (markdown); empty hides the section. Also
    /// repeats inside the public instructor modal by decision 12.</summary>
    public string? AboutText { get; set; }

    /// <summary>Heading for the Programs section (#135 fills the list).</summary>
    public string ProgramsTitle { get; set; } = "PROGRAMS";

    /// <summary>Optional intro paragraph (markdown) above the program cards.</summary>
    public string? ProgramsIntro { get; set; }

    /// <summary>Heading for the Success Stories section (#136 fills the cards).</summary>
    public string StoriesTitle { get; set; } = "SUCCESS STORIES";

    /// <summary>File-store path of the section-level stories image (#136 uploads it).</summary>
    public string? StoriesImagePath { get; set; }

    /// <summary>Optional forward-to address for contact-form messages (#138);
    /// they always land in /admin/messages regardless.</summary>
    public string? ContactForwardEmail { get; set; }
}
