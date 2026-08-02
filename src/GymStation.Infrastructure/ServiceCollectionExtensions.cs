using GymStation.Infrastructure.Attendance;
using GymStation.Infrastructure.Money;
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
        services.AddScoped<People.PersonService>();
        services.AddScoped<RankService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ScheduleService>();
        services.AddScoped<SubstitutionService>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<LedgerService>();
        services.AddScoped<Training.TrainingDiaryService>();
        services.AddScoped<Reports.ReportService>();
        services.AddScoped<Seeding.DemoSeeder>();
        services.AddSingleton<IFileStore>(new LocalFileStore(storageRoot));

        // Scoped factory, and (via the same call) the context itself as a scoped service.
        // Services share the scoped context — NotificationService relies on joining the
        // caller's SaveChanges (transactional outbox). Blazor components must NOT: the
        // renderer overlaps component inits within one request, and concurrent queries on
        // one context throw. Components take IDbContextFactory and create their own.
        services.AddDbContextFactory<GymStationDbContext>(
            o => o.UseNpgsql(connectionString),
            ServiceLifetime.Scoped);
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
        services.AddHostedService<ChargeCycleWorker>();
        return services;
    }
}
