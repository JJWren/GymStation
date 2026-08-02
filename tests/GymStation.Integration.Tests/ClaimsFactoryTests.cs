using System.Security.Claims;
using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Identity;
using GymStation.Infrastructure.Tenancy;
using GymStation.Web.Auth;
using GymStation.Web.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class ClaimsFactoryTests(PostgresFixture fixture)
{
    /// <summary>
    /// Builds Identity the way the app does, against the test database — including the
    /// custom claims principal factory. CreateAsync here is EXACTLY the path the
    /// security-stamp validator uses to regenerate the cookie principal (issue #76).
    /// </summary>
    private ServiceProvider BuildIdentity()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<GymStationDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddDbContextFactory<GymStationDbContext>(
            o => o.UseNpgsql(fixture.ConnectionString), ServiceLifetime.Scoped);
        services.AddIdentityCore<AppUser>()
            .AddEntityFrameworkStores<GymStationDbContext>()
            .AddClaimsPrincipalFactory<GymClaimsPrincipalFactory>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RegeneratedPrincipal_KeepsTheActiveGymClaim()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Claims {suffix}", Slug = $"claims-{suffix}", TimeZoneId = "UTC" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        await using var provider = BuildIdentity();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();

        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"c-{suffix}@example.test", Email = $"c-{suffix}@example.test" };
        Assert.True((await users.CreateAsync(user)).Succeeded);

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);
        await using (var context = fixture.CreateContext(tenant))
        {
            context.Persons.Add(new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "Claim",
                LastName = "Keeper",
                UserId = user.Id,
                JoinedOn = new DateOnly(2026, 1, 1),
            });
            await context.SaveChangesAsync();
        }

        user.ActiveGymId = gym.Id;
        await users.UpdateAsync(user);

        // First build (login) and a second build (what the stamp validator does later)
        // must BOTH carry the tenant claim.
        for (var round = 0; round < 2; round++)
        {
            var principal = await factory.CreateAsync(user);
            Assert.Equal(gym.Id.ToString(), principal.FindFirstValue(ActiveGymMiddleware.ActiveGymClaim));
        }
    }

    [Fact]
    public async Task StaleOrMissingGym_YieldsNoClaim()
    {
        await using var provider = BuildIdentity();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"s-{suffix}@example.test", Email = $"s-{suffix}@example.test" };
        Assert.True((await users.CreateAsync(user)).Succeeded);

        // No ActiveGymId at all → no claim.
        var principal = await factory.CreateAsync(user);
        Assert.Null(principal.FindFirstValue(ActiveGymMiddleware.ActiveGymClaim));

        // A remembered gym the user has no live membership in → still no claim.
        user.ActiveGymId = Guid.NewGuid();
        await users.UpdateAsync(user);
        principal = await factory.CreateAsync(user);
        Assert.Null(principal.FindFirstValue(ActiveGymMiddleware.ActiveGymClaim));
    }
}
