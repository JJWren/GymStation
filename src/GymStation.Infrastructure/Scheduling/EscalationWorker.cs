using GymStation.Domain.Scheduling;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymStation.Infrastructure.Scheduling;

/// <summary>
/// Periodically escalates unfilled substitution requests (T-24h) across all gyms,
/// entering each gym's tenant context to do its work.
/// </summary>
public class EscalationWorker(IServiceScopeFactory scopeFactory, ILogger<EscalationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

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
                logger.LogError(ex, "Substitution escalation pass failed.");
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
        var substitutions = scope.ServiceProvider.GetRequiredService<SubstitutionService>();

        var gymIds = await db.SubstitutionRequests.IgnoreQueryFilters()
            .Where(r => r.Status == SubstitutionStatus.Requested && r.EscalatedUtc == null)
            .Select(r => r.GymId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var gymId in gymIds)
        {
            tenant.SetGym(gymId);
            await substitutions.EscalateDueAsync(DateTimeOffset.UtcNow, ct);
        }

        tenant.Clear();
    }
}
