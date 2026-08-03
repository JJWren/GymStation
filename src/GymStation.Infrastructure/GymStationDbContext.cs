using GymStation.Domain.Attendance;
using GymStation.Domain.Events;
using GymStation.Domain.Money;
using GymStation.Domain.Notifications;
using GymStation.Domain.People;
using GymStation.Domain.Ranks;
using GymStation.Domain.Scheduling;
using GymStation.Domain.Tenancy;
using GymStation.Domain.Training;
using GymStation.Infrastructure.Identity;
using GymStation.Infrastructure.Ranks;
using GymStation.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure;

public class GymStationDbContext(DbContextOptions<GymStationDbContext> options, ITenantContext tenant)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    private readonly ITenantContext _tenant = tenant;

    /// <summary>
    /// Referenced by the query filters below. EF parameterizes DbContext instance members
    /// per query against the *current* context instance (the model cache stores the
    /// expression, not a value), so every context sees its own tenant.
    /// </summary>
    public Guid? CurrentGymId => _tenant.CurrentGymId;

    public DbSet<Gym> Gyms => Set<Gym>();
    public DbSet<GymSettings> GymSettings => Set<GymSettings>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<GuardianLink> GuardianLinks => Set<GuardianLink>();
    public DbSet<StaffProfile> StaffProfiles => Set<StaffProfile>();
    public DbSet<RankSystem> RankSystems => Set<RankSystem>();
    public DbSet<Rank> Ranks => Set<Rank>();
    public DbSet<RankAward> RankAwards => Set<RankAward>();
    public DbSet<ClassType> ClassTypes => Set<ClassType>();
    public DbSet<ClassTemplate> ClassTemplates => Set<ClassTemplate>();
    public DbSet<ClassSession> ClassSessions => Set<ClassSession>();
    public DbSet<SubstitutionRequest> SubstitutionRequests => Set<SubstitutionRequest>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();
    public DbSet<TrainingEntry> TrainingEntries => Set<TrainingEntry>();
    public DbSet<TrainingRoll> TrainingRolls => Set<TrainingRoll>();
    public DbSet<GymEvent> GymEvents => Set<GymEvent>();
    public DbSet<EventRsvp> EventRsvps => Set<EventRsvp>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Gym>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(120);
            e.Property(g => g.Slug).HasMaxLength(60);
            e.Property(g => g.TimeZoneId).HasMaxLength(60);
            e.HasIndex(g => g.Slug).IsUnique();
            e.HasOne(g => g.Settings).WithOne().HasForeignKey<GymSettings>(s => s.GymId);
        });

        builder.Entity<GymSettings>(e =>
        {
            e.HasKey(s => s.GymId);
            e.Property(s => s.AccentColorHex).HasMaxLength(9);

            // Explicit defaults so pre-existing rows land on real mat hours, not 00:00.
            e.Property(s => s.OpenTime).HasDefaultValue(new TimeOnly(6, 0));
            e.Property(s => s.CloseTime).HasDefaultValue(new TimeOnly(22, 0));
            e.HasQueryFilter(s => CurrentGymId != null && s.GymId == CurrentGymId);
        });

        builder.Entity<Person>(e =>
        {
            e.Property(p => p.FirstName).HasMaxLength(80);
            e.Property(p => p.LastName).HasMaxLength(80);
            e.Property(p => p.PhoneNumber).HasMaxLength(30);
            // One roster record per User per gym; unlimited login-less Persons.
            e.HasIndex(p => new { p.GymId, p.UserId }).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
            e.HasQueryFilter(p => CurrentGymId != null && p.GymId == CurrentGymId);
        });

        builder.Entity<GuardianLink>(e =>
        {
            e.HasIndex(l => new { l.GuardianUserId, l.ChildPersonId }).IsUnique();
            e.HasOne(l => l.ChildPerson).WithMany().HasForeignKey(l => l.ChildPersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(l => CurrentGymId != null && l.GymId == CurrentGymId);
        });

        builder.Entity<StaffProfile>(e =>
        {
            e.HasKey(p => p.PersonId);
            e.Property(p => p.Bio).HasMaxLength(2000);
            e.Property(p => p.ExperienceSummary).HasMaxLength(300);
            e.Property(p => p.PayRate).HasPrecision(10, 2);
            e.HasOne(p => p.Person).WithOne().HasForeignKey<StaffProfile>(p => p.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(p => CurrentGymId != null && p.GymId == CurrentGymId);
        });

        builder.Entity<RankSystem>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(80);
            // Platform ladders (GymId null) are visible to every tenant; custom ladders only to their own.
            e.HasQueryFilter(s => s.GymId == null || (CurrentGymId != null && s.GymId == CurrentGymId));
            e.HasData(IbjjfSeed.Systems());
        });

        builder.Entity<Rank>(e =>
        {
            e.Property(r => r.Name).HasMaxLength(60);
            e.Property(r => r.BandColorHex).HasMaxLength(9);
            e.Property(r => r.BarColorHex).HasMaxLength(9);
            e.HasIndex(r => new { r.RankSystemId, r.Order }).IsUnique();
            e.HasOne<RankSystem>().WithMany(s => s.Ranks).HasForeignKey(r => r.RankSystemId).OnDelete(DeleteBehavior.Cascade);
            // A Rank is visible iff its system is (composes with the RankSystem filter above).
            e.HasQueryFilter(r => RankSystems.Any(s => s.Id == r.RankSystemId));
            e.HasData(IbjjfSeed.Ranks());
        });

        builder.Entity<RankAward>(e =>
        {
            e.Property(a => a.Note).HasMaxLength(500);
            e.HasIndex(a => new { a.GymId, a.PersonId, a.AwardedOn });
            e.HasOne(a => a.Person).WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Rank).WithMany().HasForeignKey(a => a.RankId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(a => CurrentGymId != null && a.GymId == CurrentGymId);
        });

        builder.Entity<ClassType>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(40);
            e.Property(t => t.ColorHex).HasMaxLength(9);
            e.HasIndex(t => new { t.GymId, t.Name }).IsUnique();
            e.HasQueryFilter(t => CurrentGymId != null && t.GymId == CurrentGymId);
        });

        builder.Entity<ClassTemplate>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(80);
            e.HasMany(t => t.ClassTypes).WithMany().UsingEntity("ClassTemplateClassTypes");
            e.HasQueryFilter(t => CurrentGymId != null && t.GymId == CurrentGymId);
        });

        builder.Entity<ClassSession>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(80);
            e.Property(s => s.CancelledReason).HasMaxLength(300);
            // Idempotent lazy materialization: one session per template per date.
            e.HasIndex(s => new { s.GymId, s.TemplateId, s.Date }).IsUnique().HasFilter("\"TemplateId\" IS NOT NULL");
            e.HasIndex(s => new { s.GymId, s.Date });
            e.HasMany(s => s.ClassTypes).WithMany().UsingEntity("ClassSessionClassTypes");
            e.HasQueryFilter(s => CurrentGymId != null && s.GymId == CurrentGymId);
        });

        builder.Entity<SubstitutionRequest>(e =>
        {
            e.Property(r => r.Note).HasMaxLength(300);
            e.HasOne(r => r.Session).WithMany().HasForeignKey(r => r.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.GymId, r.Status });
            e.HasQueryFilter(r => CurrentGymId != null && r.GymId == CurrentGymId);
        });

        builder.Entity<Notification>(e =>
        {
            e.Property(n => n.Title).HasMaxLength(150);
            e.Property(n => n.Body).HasMaxLength(1000);
            e.Property(n => n.LinkPath).HasMaxLength(200);
            e.HasIndex(n => new { n.RecipientUserId, n.ReadUtc });
            e.HasMany(n => n.Deliveries).WithOne().HasForeignKey(d => d.NotificationId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(n => CurrentGymId != null && n.GymId == CurrentGymId);
        });

        builder.Entity<NotificationDelivery>(e =>
        {
            e.Property(d => d.Error).HasMaxLength(500);
            e.HasIndex(d => new { d.Status, d.Channel });
            e.HasQueryFilter(d => CurrentGymId != null && d.GymId == CurrentGymId);
        });

        builder.Entity<AttendanceRecord>(e =>
        {
            e.HasIndex(a => new { a.SessionId, a.PersonId }).IsUnique();
            e.HasIndex(a => new { a.GymId, a.PersonId, a.Status });
            e.HasOne(a => a.Session).WithMany().HasForeignKey(a => a.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Person).WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(a => CurrentGymId != null && a.GymId == CurrentGymId);
        });

        builder.Entity<MembershipPlan>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(80);
            e.Property(p => p.Price).HasPrecision(10, 2);
            e.HasIndex(p => new { p.GymId, p.Name }).IsUnique();
            e.HasQueryFilter(p => CurrentGymId != null && p.GymId == CurrentGymId);
        });

        builder.Entity<Charge>(e =>
        {
            e.Property(c => c.Amount).HasPrecision(10, 2);
            e.Property(c => c.Description).HasMaxLength(150);
            e.Property(c => c.CycleKey).HasMaxLength(7);
            // One cycle charge per person per month; the ledger can never double-bill a cycle.
            e.HasIndex(c => new { c.GymId, c.PersonId, c.CycleKey }).IsUnique().HasFilter("\"CycleKey\" IS NOT NULL");
            e.HasOne(c => c.Person).WithMany().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(c => CurrentGymId != null && c.GymId == CurrentGymId);
        });

        builder.Entity<Payment>(e =>
        {
            e.Property(p => p.Amount).HasPrecision(10, 2);
            e.Property(p => p.Note).HasMaxLength(300);
            e.Property(p => p.VoidReason).HasMaxLength(300);
            e.Ignore(p => p.Voided);
            e.HasOne(p => p.Person).WithMany().HasForeignKey(p => p.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(p => CurrentGymId != null && p.GymId == CurrentGymId);
        });

        builder.Entity<ExpenseCategory>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(60);
            e.HasIndex(c => new { c.GymId, c.Name }).IsUnique();
            e.HasQueryFilter(c => CurrentGymId != null && c.GymId == CurrentGymId);
        });

        builder.Entity<Expense>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.GymId, x.SpentOn });
            // One materialized row per recurring expense per date — reruns can't double-log.
            e.HasIndex(x => new { x.RecurringExpenseId, x.SpentOn }).IsUnique().HasFilter("\"RecurringExpenseId\" IS NOT NULL");
            e.HasQueryFilter(x => CurrentGymId != null && x.GymId == CurrentGymId);
        });

        builder.Entity<RecurringExpense>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentGymId != null && x.GymId == CurrentGymId);
        });

        builder.Entity<TrainingEntry>(e =>
        {
            e.Property(t => t.Notes).HasMaxLength(4000);
            e.HasIndex(t => new { t.GymId, t.PersonId, t.EntryDate });
            e.HasMany(t => t.Rolls).WithOne().HasForeignKey(r => r.TrainingEntryId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(t => CurrentGymId != null && t.GymId == CurrentGymId);
        });

        builder.Entity<TrainingRoll>(e =>
        {
            e.Property(r => r.PartnerLabel).HasMaxLength(120);
            e.Property(r => r.Summary).HasMaxLength(300);
            e.HasQueryFilter(r => CurrentGymId != null && r.GymId == CurrentGymId);
        });

        builder.Entity<GymEvent>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.TimeInfo).HasMaxLength(100);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.Details).HasMaxLength(2000);
            e.HasIndex(x => new { x.GymId, x.StartsOn });
            e.HasQueryFilter(x => CurrentGymId != null && x.GymId == CurrentGymId);
        });

        builder.Entity<EventRsvp>(e =>
        {
            e.HasIndex(x => new { x.EventId, x.PersonId }).IsUnique();
            e.HasOne<GymEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentGymId != null && x.GymId == CurrentGymId);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // The query filters make other tenants invisible; this guard makes them unwritable.
    private void GuardTenantWrites()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (_tenant.CurrentGymId is not { } gymId)
            {
                throw new InvalidOperationException(
                    $"Writing {entry.Metadata.ClrType.Name} requires an active tenant.");
            }

            if (entry.State == EntityState.Added && entry.Entity.GymId == Guid.Empty)
            {
                entry.Entity.GymId = gymId;
            }

            if (entry.Entity.GymId != gymId)
            {
                throw new InvalidOperationException(
                    $"Cross-tenant write blocked: {entry.Metadata.ClrType.Name} belongs to gym {entry.Entity.GymId}, active tenant is {gymId}.");
            }
        }
    }
}
