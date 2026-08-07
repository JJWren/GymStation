namespace GymStation.Domain.People;

/// <summary>
/// The owner-grantable admin capabilities (#217). One grant row per capability
/// per Person; Owners hold every capability implicitly and never carry rows.
/// Values are stable ints — they live in the database.
/// </summary>
public enum GymCapability
{
    ViewFinances = 1,
    ManageFinances = 2,
    ViewReports = 3,
    ManageRoster = 4,
    ManageRanks = 5,
    ManageSchedule = 6,
    ManageEvents = 7,
    ManageMessaging = 8,
    EditLanding = 9,
    ManageSettings = 10,
}

/// <summary>A Gym's grant of one capability to one staff-ish Person.</summary>
public class PermissionGrant : Tenancy.ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }
    public Guid PersonId { get; set; }
    public GymCapability Capability { get; set; }
}

/// <summary>Named starting bundles — templates the owner applies, then tweaks.</summary>
public static class CapabilityPresets
{
    public static readonly IReadOnlyDictionary<string, GymCapability[]> All = new Dictionary<string, GymCapability[]>
    {
        ["full-admin"] = Enum.GetValues<GymCapability>(),
        ["front-desk"] = [GymCapability.ManageRoster, GymCapability.ManageSchedule, GymCapability.ManageEvents, GymCapability.ManageMessaging],
        ["coach-plus"] = [GymCapability.ManageRanks, GymCapability.ManageSchedule, GymCapability.ManageRoster],
    };
}
