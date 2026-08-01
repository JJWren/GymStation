using GymStation.Domain.Attendance;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymStation.Infrastructure.Attendance;

/// <summary>Applies soft approval across all gyms: pending → confirmed at session end + 2h.</summary>
public class AutoConfirmWorker(IServiceScopeFactory scopeFactory, ILogger<AutoConfirmWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Attendance auto-confirm pass failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task RunPassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymStationDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var attendance = scope.ServiceProvider.GetRequiredService<AttendanceService>();

        var gymIds = await db.AttendanceRecords.IgnoreQueryFilters()
            .Where(a => a.Status == AttendanceStatus.Pending)
            .Select(a => a.GymId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var gymId in gymIds)
        {
            tenant.SetGym(gymId);
            await attendance.ConfirmDueAsync(DateTimeOffset.UtcNow, ct);
        }

        tenant.Clear();
    }
}
