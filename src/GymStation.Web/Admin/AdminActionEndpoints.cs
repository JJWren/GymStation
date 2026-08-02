using System.Security.Claims;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Money;
using GymStation.Infrastructure.Scheduling;
using GymStation.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Admin;

/// <summary>Row-level admin form actions for the schedule surface.</summary>
public static class AdminActionEndpoints
{
    public static IEndpointRouteBuilder MapAdminActionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/actions")
            .RequireAuthorization("GymStaff")
            .ValidateAntiforgery();

        group.MapPost("/cancel-session", async ([FromForm] Guid sessionId, [FromForm] string reason, ScheduleService schedule) =>
        {
            await schedule.CancelSessionAsync(sessionId, string.IsNullOrWhiteSpace(reason) ? "No reason given" : reason);
            return Results.Redirect("/admin/schedule");
        });

        group.MapPost("/reopen-session", async ([FromForm] Guid sessionId, ScheduleService schedule) =>
        {
            await schedule.ReopenSessionAsync(sessionId);
            return Results.Redirect("/admin/schedule");
        });

        group.MapPost("/approve-sub", async ([FromForm] Guid requestId, SubstitutionService subs) =>
        {
            await subs.ApproveAsync(requestId);
            return Results.Redirect("/admin/schedule");
        });

        group.MapPost("/decline-sub", async ([FromForm] Guid requestId, SubstitutionService subs) =>
        {
            await subs.DeclineAsync(requestId);
            return Results.Redirect("/admin/schedule");
        });

        group.MapPost("/swap-mode", async ([FromForm] string mode, GymStationDbContext db) =>
        {
            var settings = await db.GymSettings.SingleAsync();
            settings.SubstitutionMode = mode == "admin-gate" ? SubstitutionMode.AdminGate : SubstitutionMode.AutoApply;
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/schedule");
        });

        group.MapPost("/open-claims", async ([FromForm] bool enabled, GymStationDbContext db) =>
        {
            var settings = await db.GymSettings.SingleAsync();
            settings.OpenClaimsEnabled = enabled;
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/schedule");
        });

        group.MapPost("/record-payment", async (
            [FromForm] Guid personId,
            [FromForm] decimal amount,
            [FromForm] string? back,
            ClaimsPrincipal user,
            GymStationDbContext db,
            LedgerService ledger) =>
        {
            var destination = back == "person" ? $"/admin/people/{personId}" : "/admin/dues";
            if (amount <= 0)
            {
                return Results.Redirect($"{destination}?failed=1");
            }

            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid? recordedBy = null;
            if (Guid.TryParse(raw, out var userId))
            {
                recordedBy = (await db.Persons.SingleOrDefaultAsync(p => p.UserId == userId))?.Id;
            }

            // Received date in gym-local time, matching /run-cycle (UTC could shift the day/month).
            var gym = await db.Gyms.SingleAsync(g => g.Id == db.CurrentGymId);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(gym.TimeZoneId);
            var receivedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);

            try
            {
                await ledger.RecordPaymentAsync(personId, amount, receivedOn, recordedBy, null);
            }
            catch (InvalidOperationException)
            {
                // Person not visible in the active gym (stale form) — same failure surface.
                return Results.Redirect($"{destination}?failed=1");
            }

            return Results.Redirect(destination);
        });

        group.MapPost("/run-cycle", async (GymStationDbContext db, LedgerService ledger) =>
        {
            var gym = await db.Gyms.SingleAsync(g => g.Id == db.CurrentGymId);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(gym.TimeZoneId);
            var gymToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
            var raised = await ledger.RaiseMonthlyChargesAsync(gymToday);

            // The button means "run the cycle" — recurring expenses are part of it,
            // exactly like the background worker's pass.
            await ledger.MaterializeRecurringExpensesAsync(gymToday);
            return Results.Redirect($"/admin/dues?raised={raised}");
        });

        group.MapPost("/void-payment", async (
            [FromForm] Guid paymentId,
            [FromForm] Guid personId,
            [FromForm] string reason,
            ClaimsPrincipal user,
            GymStationDbContext db,
            LedgerService ledger) =>
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid? voidedBy = null;
            if (Guid.TryParse(raw, out var userId))
            {
                voidedBy = (await db.Persons.SingleOrDefaultAsync(p => p.UserId == userId))?.Id;
            }

            try
            {
                await ledger.VoidPaymentAsync(paymentId, voidedBy, reason);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/people/{personId}?failed=1");
            }

            return Results.Redirect($"/admin/people/{personId}");
        });

        group.MapPost("/update-plan", async ([FromForm] Guid planId, [FromForm] string name, [FromForm] decimal price, LedgerService ledger) =>
        {
            try
            {
                await ledger.UpdatePlanAsync(planId, name, price);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/dues?failed=2");
            }

            return Results.Redirect("/admin/dues");
        });

        group.MapPost("/plan-archived", async ([FromForm] Guid planId, [FromForm] bool archived, LedgerService ledger) =>
        {
            try
            {
                await ledger.SetPlanArchivedAsync(planId, archived);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/dues?failed=2");
            }

            return Results.Redirect("/admin/dues");
        });

        group.MapPost("/rename-category", async ([FromForm] Guid categoryId, [FromForm] string name, LedgerService ledger) =>
        {
            try
            {
                await ledger.RenameCategoryAsync(categoryId, name);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/dues?failed=3");
            }

            return Results.Redirect("/admin/dues");
        });

        group.MapPost("/category-archived", async ([FromForm] Guid categoryId, [FromForm] bool archived, LedgerService ledger) =>
        {
            try
            {
                await ledger.SetCategoryArchivedAsync(categoryId, archived);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/dues?failed=3");
            }

            return Results.Redirect("/admin/dues");
        });

        group.MapPost("/add-recurring", async ([FromForm] Guid categoryId, [FromForm] decimal amount, [FromForm] int dayOfMonth, [FromForm] string? note, LedgerService ledger) =>
        {
            try
            {
                await ledger.AddRecurringExpenseAsync(categoryId, amount, dayOfMonth, note);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/dues?failed=4");
            }

            return Results.Redirect("/admin/dues");
        });

        group.MapPost("/recurring-active", async ([FromForm] Guid recurringId, [FromForm] bool active, LedgerService ledger) =>
        {
            try
            {
                await ledger.SetRecurringActiveAsync(recurringId, active);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/dues?failed=4");
            }

            return Results.Redirect("/admin/dues");
        });

        group.MapPost("/assign-plan", async ([FromForm] Guid personId, [FromForm] Guid? planId, GymStationDbContext db) =>
        {
            var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId);
            if (person is null)
            {
                return Results.Redirect("/admin/roster");
            }

            if (planId is { } id && !await db.MembershipPlans.AnyAsync(pl => pl.Id == id && !pl.Archived))
            {
                return Results.Redirect($"/admin/people/{personId}?failed=1");
            }

            person.MembershipPlanId = planId;
            await db.SaveChangesAsync();
            return Results.Redirect($"/admin/people/{personId}");
        });

        return app;
    }
}
