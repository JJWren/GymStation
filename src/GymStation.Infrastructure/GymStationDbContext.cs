using GymStation.Domain.People;
using GymStation.Domain.Ranks;
using GymStation.Domain.Tenancy;
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
    public DbSet<InstructorProfile> InstructorProfiles => Set<InstructorProfile>();
    public DbSet<RankSystem> RankSystems => Set<RankSystem>();
    public DbSet<Rank> Ranks => Set<Rank>();
    public DbSet<RankAward> RankAwards => Set<RankAward>();

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
            e.HasQueryFilter(s => CurrentGymId != null && s.GymId == CurrentGymId);
        });

        builder.Entity<Person>(e =>
        {
            e.Property(p => p.FirstName).HasMaxLength(80);
            e.Property(p => p.LastName).HasMaxLength(80);
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

        builder.Entity<InstructorProfile>(e =>
        {
            e.HasKey(p => p.PersonId);
            e.Property(p => p.Bio).HasMaxLength(2000);
            e.Property(p => p.ExperienceSummary).HasMaxLength(300);
            e.Property(p => p.PayRate).HasPrecision(10, 2);
            e.HasOne(p => p.Person).WithOne().HasForeignKey<InstructorProfile>(p => p.PersonId).OnDelete(DeleteBehavior.Cascade);
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
