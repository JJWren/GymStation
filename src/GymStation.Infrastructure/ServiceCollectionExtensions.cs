using GymStation.Infrastructure.Ranks;
using GymStation.Infrastructure.Storage;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymStation.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGymStationData(this IServiceCollection services, string connectionString, string storageRoot)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<GymMembershipService>();
        services.AddScoped<RankService>();
        services.AddSingleton<IFileStore>(new LocalFileStore(storageRoot));
        services.AddDbContext<GymStationDbContext>(o => o.UseNpgsql(connectionString));
        return services;
    }
}
