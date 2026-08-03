using GymStation.Domain.Tenancy;

namespace GymStation.Domain.People;

public enum PayRateUnit
{
    PerClass = 0,
    Hourly = 1,
    Monthly = 2,
}

/// <summary>
/// Profile data for anyone the gym pays or fronts publicly — the Instructor,
/// Admin, Owner, or Staff roles (formerly InstructorProfile). Pay is stored +
/// displayed only; payroll computation stays on the future board.
/// </summary>
public class StaffProfile : ITenantOwned
{
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public Guid GymId { get; set; }

    public string? Bio { get; set; }

    /// <summary>e.g. "20+ years on the mat", "3x Pan Ams veteran".</summary>
    public string? ExperienceSummary { get; set; }

    /// <summary>Shown in the public instructor modal when filled (#137).</summary>
    public string? Hobbies { get; set; }

    public decimal? PayRate { get; set; }
    public PayRateUnit PayRateUnit { get; set; } = PayRateUnit.PerClass;
}
