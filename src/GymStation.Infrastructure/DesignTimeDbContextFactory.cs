using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GymStation.Infrastructure;

/// <summary>For `dotnet ef` only — never resolved at runtime; the connection string is not used to connect during migration generation.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GymStationDbContext>
{
    public GymStationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GymStationDbContext>()
            .UseNpgsql("Host=localhost;Database=gymstation;Username=postgres")
            .Options;

        return new GymStationDbContext(options, new TenantContext());
    }
}
