using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GymStation.Infrastructure;

/// <summary>
/// For `dotnet ef` only — never resolved at runtime. Script generation is offline and
/// ignores the connection string; `database update` connects, so it honors
/// ConnectionStrings__Default when set (falling back to a local default).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GymStationDbContext>
{
    public GymStationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=gymstation;Username=postgres";

        var options = new DbContextOptionsBuilder<GymStationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new GymStationDbContext(options, new TenantContext());
    }
}
