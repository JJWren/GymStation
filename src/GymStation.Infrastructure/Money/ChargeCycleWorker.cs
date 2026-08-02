using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymStation.Infrastructure.Money;

/// <summary>
/// Raises the current month's cycle charges for every gym. Raising is idempotent, so the
/// worker simply ensures each gym-local current month has been cycled — catching up gyms
/// created (or servers down) past the 1st.
/// </summary>
public class ChargeCycleWorker(IServiceScopeFactory scopeFactory, ILogger<ChargeCycleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

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
                logger.LogError(ex, "Charge cycle pass failed.");
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
        var ledger = scope.ServiceProvider.GetRequiredService<LedgerService>();

        var gyms = await db.Gyms.Select(g => new { g.Id, g.TimeZoneId }).ToListAsync(ct);

        foreach (var gym in gyms)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(gym.TimeZoneId);
                var gymToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);

                tenant.SetGym(gym.Id);
                await ledger.RaiseMonthlyChargesAsync(gymToday, ct);
                await ledger.MaterializeRecurringExpensesAsync(gymToday, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One gym's bad data (e.g. timezone id) must not starve the rest of the pass.
                logger.LogError(ex, "Charge cycle failed for gym {GymId}.", gym.Id);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        tenant.Clear();
    }
}
