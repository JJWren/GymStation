using GymStation.Infrastructure.Attendance;
using GymStation.Infrastructure.Notifications;
using GymStation.Infrastructure.Ranks;
using GymStation.Infrastructure.Scheduling;
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
        services.AddScoped<NotificationService>();
        services.AddScoped<ScheduleService>();
        services.AddScoped<SubstitutionService>();
        services.AddScoped<AttendanceService>();
        services.AddSingleton<IFileStore>(new LocalFileStore(storageRoot));
        services.AddDbContext<GymStationDbContext>(o => o.UseNpgsql(connectionString));
        return services;
    }

    public static IServiceCollection AddGymStationEmail(this IServiceCollection services, EmailOptions options)
    {
        services.AddSingleton(options);
        if (options.Configured)
        {
            services.AddSingleton<IEmailDeliverer, SmtpEmailDeliverer>();
        }
        else
        {
            services.AddSingleton<IEmailDeliverer, LoggingEmailDeliverer>();
        }

        return services;
    }

    /// <summary>Background workers — registered by the host, not by tests.</summary>
    public static IServiceCollection AddGymStationWorkers(this IServiceCollection services)
    {
        services.AddHostedService<NotificationDispatcher>();
        services.AddHostedService<EscalationWorker>();
        services.AddHostedService<AutoConfirmWorker>();
        return services;
    }
}
