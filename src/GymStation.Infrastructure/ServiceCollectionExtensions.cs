using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymStation.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGymStationData(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<GymMembershipService>();
        services.AddDbContext<GymStationDbContext>(o => o.UseNpgsql(connectionString));
        return services;
    }
}
