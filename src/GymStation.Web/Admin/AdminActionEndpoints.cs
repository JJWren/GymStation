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
        // One route space, one capability-gated subgroup per concern (#217).
        // The policies are the security boundary; nav hiding is just UX.
        var messaging = app.MapGroup("/admin/actions").RequireAuthorization("Cap:ManageMessaging").ValidateAntiforgery();
        var landing = app.MapGroup("/admin/actions").RequireAuthorization("Cap:EditLanding").ValidateAntiforgery();
        var roster = app.MapGroup("/admin/actions").RequireAuthorization("Cap:ManageRoster").ValidateAntiforgery();
        var schedule = app.MapGroup("/admin/actions").RequireAuthorization("Cap:ManageSchedule").ValidateAntiforgery();
        var settings = app.MapGroup("/admin/actions").RequireAuthorization("Cap:ManageSettings").ValidateAntiforgery();
        var finance = app.MapGroup("/admin/actions").RequireAuthorization("Cap:ManageFinances").ValidateAntiforgery();

        // Contact-message read state (#138): per-message toggle.
        messaging.MapPost("/message-read", async ([FromForm] Guid messageId, [FromForm] bool read, GymStationDbContext db) =>
        {
            var message = await db.ContactMessages.SingleOrDefaultAsync(m => m.Id == messageId);
            if (message is not null)
            {
                message.ReadUtc = read ? DateTimeOffset.UtcNow : null;
                await db.SaveChangesAsync();
            }

            return Results.Redirect("/admin/messages");
        });

        // Landing section ordering (#134): one step at a time. Tampered requests
        // are rejected outright — a junk key must not trigger a silent write of
        // the normalized order.
        landing.MapPost("/landing-section-move", async ([FromForm] string key, [FromForm] int direction, GymStationDbContext db) =>
        {
            if (!LandingSections.Default.Contains(key?.ToLowerInvariant() ?? "") || direction is not (-1 or 1))
            {
                return Results.Redirect("/admin/landing?failed=1");
            }

            var settings = await db.GymSettings.SingleOrDefaultAsync();
            if (settings is not null)
            {
                settings.SectionOrder = LandingSections.Move(settings.SectionOrder, key!, direction);
                await db.SaveChangesAsync();
            }

            return Results.Redirect("/admin/landing");
        });

        roster.MapPost("/rename-person", async (
            [FromForm] Guid personId, [FromForm] string firstName, [FromForm] string lastName,
            GymStation.Infrastructure.People.PersonService people) =>
        {
            try
            {
                await people.SetNameAsync(personId, firstName, lastName);
                return Results.Redirect($"/admin/people/{personId}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/people/{personId}?failed=1");
            }
        });

        // #191: post-hoc login link/unlink on an existing Person. Email→User
        // resolution stays here at the edge (roster add-form precedent) — the
        // service speaks Guids.
        roster.MapPost("/link-login", async (
            [FromForm] Guid personId, [FromForm] string email,
            Microsoft.AspNetCore.Identity.UserManager<GymStation.Infrastructure.Identity.AppUser> users,
            GymStation.Infrastructure.People.PersonService people) =>
        {
            var user = await users.FindByEmailAsync(email.Trim());
            if (user is null)
            {
                return Results.Redirect($"/admin/people/{personId}?linkfailed=1");
            }

            try
            {
                await people.LinkLoginAsync(personId, user.Id);
                return Results.Redirect($"/admin/people/{personId}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/people/{personId}?linkfailed=1");
            }
        });

        roster.MapPost("/unlink-login", async (
            [FromForm] Guid personId, GymStation.Infrastructure.People.PersonService people) =>
        {
            try
            {
                await people.UnlinkLoginAsync(personId);
            }
            catch (InvalidOperationException)
            {
                // Missing person: the goal state (no link) holds either way.
            }

            return Results.Redirect($"/admin/people/{personId}");
        });

        schedule.MapPost("/cancel-session", async ([FromForm] Guid sessionId, [FromForm] string reason, ScheduleService schedule) =>
        {
            await schedule.CancelSessionAsync(sessionId, string.IsNullOrWhiteSpace(reason) ? "No reason given" : reason);
            return Results.Redirect("/admin/schedule");
        });

        schedule.MapPost("/duplicate-session", async ([FromForm] Guid sessionId, [FromForm] DateOnly date, [FromForm] string start, ScheduleService schedule) =>
        {
            if (!TimeOnly.TryParseExact(start, "HH\\:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var startTime))
            {
                return Results.Redirect($"/admin/schedule?start={GymStation.Domain.Scheduling.Weeks.WeekOf(date):yyyy-MM-dd}&dupfailed=1");
            }

            try
            {
                var copyId = await schedule.DuplicateSessionAsync(sessionId, date, startTime);
                // Land on the copy's week with its editor open — visible confirmation.
                return Results.Redirect($"/admin/schedule?start={GymStation.Domain.Scheduling.Weeks.WeekOf(date):yyyy-MM-dd}&edit={copyId}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/schedule?start={GymStation.Domain.Scheduling.Weeks.WeekOf(date):yyyy-MM-dd}&dupfailed=1");
            }
        });

        schedule.MapPost("/duplicate-template", async (
            [FromForm] Guid templateId, [FromForm] string day, [FromForm] string start, [FromForm] string week,
            ScheduleService schedule) =>
        {
            var weekStart = DateOnly.TryParseExact(week, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedWeek)
                ? GymStation.Domain.Scheduling.Weeks.WeekOf(parsedWeek)
                : GymStation.Domain.Scheduling.Weeks.WeekOf(DateOnly.FromDateTime(DateTime.UtcNow));

            // Enum.TryParse happily accepts out-of-range numerics ("9") — IsDefined
            // keeps a tampered weekday from minting a template that never matches.
            if (!Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var dayOfWeek)
                || !Enum.IsDefined(dayOfWeek)
                || !TimeOnly.TryParseExact(start, "HH\\:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var startTime))
            {
                return Results.Redirect($"/admin/schedule?start={weekStart:yyyy-MM-dd}&tdupfailed=1");
            }

            try
            {
                var firstOccurrenceId = await schedule.DuplicateTemplateAsync(templateId, dayOfWeek, startTime, weekStart);
                // Land on the copy's first occurrence with its editor open — the
                // template section inside it confirms the new weekly class.
                return Results.Redirect($"/admin/schedule?start={weekStart:yyyy-MM-dd}&edit={firstOccurrenceId}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/schedule?start={weekStart:yyyy-MM-dd}&tdupfailed=1");
            }
        });

        schedule.MapPost("/promote-session", async ([FromForm] Guid sessionId, [FromForm] string? week, ScheduleService schedule) =>
        {
            var back = DateOnly.TryParseExact(week, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed) ? $"start={parsed:yyyy-MM-dd}&" : "";
            try
            {
                await schedule.PromoteToTemplateAsync(sessionId);
                // Reopen the same class — its editor now shows the weekly-template
                // section, which IS the visible confirmation.
                return Results.Redirect($"/admin/schedule?{back}edit={sessionId}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/schedule?{back}promotefailed=1");
            }
        });

        schedule.MapPost("/delete-session", async ([FromForm] Guid sessionId, [FromForm] string? week, ScheduleService schedule) =>
        {
            // Only a date the page itself rendered rides back into the redirect.
            var back = DateOnly.TryParseExact(week, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed) ? $"?start={parsed:yyyy-MM-dd}" : "";
            try
            {
                await schedule.DeleteSessionAsync(sessionId);
                return Results.Redirect($"/admin/schedule{back}");
            }
            catch (InvalidOperationException)
            {
                // Delete is idempotent on a missing session, so the ONLY refusal
                // left is recorded history — the banner names exactly that.
                return Results.Redirect($"/admin/schedule{back}{(back.Length > 0 ? "&" : "?")}delfailed=1");
            }
        });

        schedule.MapPost("/reopen-session", async ([FromForm] Guid sessionId, ScheduleService schedule) =>
        {
            await schedule.ReopenSessionAsync(sessionId);
            return Results.Redirect("/admin/schedule");
        });

        schedule.MapPost("/approve-sub", async ([FromForm] Guid requestId, SubstitutionService subs) =>
        {
            await subs.ApproveAsync(requestId);
            return Results.Redirect("/admin/schedule");
        });

        schedule.MapPost("/decline-sub", async ([FromForm] Guid requestId, SubstitutionService subs) =>
        {
            await subs.DeclineAsync(requestId);
            return Results.Redirect("/admin/schedule");
        });

        settings.MapPost("/swap-mode", async ([FromForm] string mode, GymStationDbContext db) =>
        {
            // Only the two known values may flip the setting — a tampered or stale
            // form must not silently land the gym on AutoApply.
            SubstitutionMode? target = mode switch
            {
                "admin-gate" => SubstitutionMode.AdminGate,
                "auto-apply" => SubstitutionMode.AutoApply,
                _ => null,
            };
            if (target is null)
            {
                return Results.Redirect("/admin/settings?failed=1");
            }

            var settings = await db.GymSettings.SingleAsync();
            settings.SubstitutionMode = target.Value;
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/settings");
        });

        settings.MapPost("/open-claims", async ([FromForm] bool enabled, GymStationDbContext db) =>
        {
            var settings = await db.GymSettings.SingleAsync();
            settings.OpenClaimsEnabled = enabled;
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/settings");
        });

        finance.MapPost("/record-payment", async (
            [FromForm] Guid personId,
            [FromForm] decimal amount,
            [FromForm] string? back,
            ClaimsPrincipal user,
            GymStationDbContext db,
            LedgerService ledger) =>
        {
            var destination = back == "person" ? $"/admin/people/{personId}" : "/admin/finance";
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

        finance.MapPost("/void-payment", async (
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

        finance.MapPost("/update-expense", async (
            [FromForm] Guid expenseId, [FromForm] Guid categoryId, [FromForm] decimal amount,
            [FromForm] DateOnly spentOn, [FromForm] string? note, LedgerService ledger) =>
        {
            try
            {
                await ledger.UpdateExpenseAsync(expenseId, categoryId, amount, spentOn, note);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=2");
            }

            return Results.Redirect("/admin/finance");
        });

        finance.MapPost("/delete-expense", async ([FromForm] Guid expenseId, LedgerService ledger) =>
        {
            try
            {
                await ledger.DeleteExpenseAsync(expenseId);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=2");
            }

            return Results.Redirect("/admin/finance");
        });

        finance.MapPost("/add-income", async (
            [FromForm] string label, [FromForm] decimal amount, [FromForm] DateOnly receivedOn,
            [FromForm] string? note, LedgerService ledger) =>
        {
            try
            {
                await ledger.AddOtherIncomeAsync(label, amount, receivedOn, note);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=2");
            }

            return Results.Redirect("/admin/finance");
        });

        finance.MapPost("/update-income", async (
            [FromForm] Guid incomeId, [FromForm] string label, [FromForm] decimal amount,
            [FromForm] DateOnly receivedOn, [FromForm] string? note, LedgerService ledger) =>
        {
            try
            {
                await ledger.UpdateOtherIncomeAsync(incomeId, label, amount, receivedOn, note);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=2");
            }

            return Results.Redirect("/admin/finance");
        });

        finance.MapPost("/delete-income", async ([FromForm] Guid incomeId, LedgerService ledger) =>
        {
            try
            {
                await ledger.DeleteOtherIncomeAsync(incomeId);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=2");
            }

            return Results.Redirect("/admin/finance");
        });

        finance.MapPost("/update-plan", async (
            [FromForm] Guid planId, [FromForm] string name, [FromForm] decimal price,
            [FromForm] int? includedAdults, [FromForm] int? includedKids,
            [FromForm] decimal? extraAdultPrice, [FromForm] decimal? extraKidPrice,
            LedgerService ledger) =>
        {
            try
            {
                await ledger.UpdatePlanAsync(planId, name, price, includedAdults, includedKids, extraAdultPrice, extraKidPrice);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=2");
            }

            return Results.Redirect("/admin/finance");
        });

        finance.MapPost("/plan-archived", async ([FromForm] Guid planId, [FromForm] bool archived, LedgerService ledger) =>
        {
            try
            {
                await ledger.SetPlanArchivedAsync(planId, archived);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=2");
            }

            return Results.Redirect("/admin/finance");
        });

        finance.MapPost("/rename-category", async ([FromForm] Guid categoryId, [FromForm] string name, LedgerService ledger) =>
        {
            try
            {
                await ledger.RenameCategoryAsync(categoryId, name);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=3");
            }

            return Results.Redirect("/admin/finance");
        });

        finance.MapPost("/category-archived", async ([FromForm] Guid categoryId, [FromForm] bool archived, LedgerService ledger) =>
        {
            try
            {
                await ledger.SetCategoryArchivedAsync(categoryId, archived);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=3");
            }

            return Results.Redirect("/admin/finance");
        });

        finance.MapPost("/add-recurring", async ([FromForm] Guid categoryId, [FromForm] decimal amount, [FromForm] int dayOfMonth, [FromForm] string? note, LedgerService ledger) =>
        {
            try
            {
                await ledger.AddRecurringExpenseAsync(categoryId, amount, dayOfMonth, note);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=4");
            }

            return Results.Redirect("/admin/finance");
        });

        finance.MapPost("/recurring-active", async ([FromForm] Guid recurringId, [FromForm] bool active, LedgerService ledger) =>
        {
            try
            {
                await ledger.SetRecurringActiveAsync(recurringId, active);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/finance?failed=4");
            }

            return Results.Redirect("/admin/finance");
        });

        // #196: the person-side guard SetFamilyPlanAsync always had — scope
        // validated, visitors converted by assignment.
        roster.MapPost("/assign-plan", async (
            [FromForm] Guid personId, [FromForm] Guid? planId, GymStation.Infrastructure.People.PersonService people) =>
        {
            try
            {
                await people.AssignPlanAsync(personId, planId);
                return Results.Redirect($"/admin/people/{personId}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/people/{personId}?failed=1");
            }
        });

        // Capability management (#217): the OWNER alone grants and revokes.
        // Checkboxes post as repeated caps values; unchecked simply post nothing,
        // so an empty selection clears every grant.
        var owner = app.MapGroup("/admin/actions").RequireAuthorization("GymOwner").ValidateAntiforgery();

        owner.MapPost("/set-permissions", async (
            HttpRequest request, [FromForm] Guid personId,
            GymStation.Infrastructure.People.PermissionService permissions) =>
        {
            var form = await request.ReadFormAsync();
            var capabilities = new List<GymStation.Domain.People.GymCapability>();
            foreach (var value in form["caps"])
            {
                if (!int.TryParse(value, out var parsed)
                    || !Enum.IsDefined((GymStation.Domain.People.GymCapability)parsed))
                {
                    return Results.Redirect($"/admin/people/{personId}?failed=1");
                }

                capabilities.Add((GymStation.Domain.People.GymCapability)parsed);
            }

            try
            {
                await permissions.SetForPersonAsync(personId, capabilities);
                return Results.Redirect($"/admin/people/{personId}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/people/{personId}?failed=1");
            }
        });

        owner.MapPost("/apply-permission-preset", async (
            [FromForm] Guid personId, [FromForm] string preset,
            GymStation.Infrastructure.People.PermissionService permissions) =>
        {
            try
            {
                await permissions.ApplyPresetAsync(personId, preset);
                return Results.Redirect($"/admin/people/{personId}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/people/{personId}?failed=1");
            }
        });

        return app;
    }
}
