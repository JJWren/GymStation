using System.Security.Claims;
using GymStation.Domain.Attendance;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Attendance;
using GymStation.Infrastructure.Scheduling;
using GymStation.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Instructor;

/// <summary>Instructor-side substitution actions (request cover, claim, accept, withdraw).</summary>
public static class InstructorActionEndpoints
{
    public static IEndpointRouteBuilder MapInstructorActionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/instructor/actions")
            .RequireAuthorization("GymInstructor")
            .ValidateAntiforgery();

        group.MapPost("/request-sub", async (
            [FromForm] Guid sessionId,
            [FromForm] Guid? proposedSubPersonId,
            [FromForm] string? note,
            ClaimsPrincipal user,
            GymStationDbContext db,
            SubstitutionService subs) =>
        {
            var personId = await PersonIdAsync(db, user);
            if (personId is null)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            try
            {
                await subs.RequestAsync(sessionId, personId.Value, proposedSubPersonId, note);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            return Results.Redirect("/instructor/swaps");
        });

        group.MapPost("/accept-sub", async ([FromForm] Guid requestId, ClaimsPrincipal user, GymStationDbContext db, SubstitutionService subs) =>
        {
            var personId = await PersonIdAsync(db, user);
            if (personId is null)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            try
            {
                await subs.AcceptAsync(requestId, personId.Value);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            return Results.Redirect("/instructor/swaps");
        });

        group.MapPost("/withdraw-sub", async ([FromForm] Guid requestId, ClaimsPrincipal user, GymStationDbContext db, SubstitutionService subs) =>
        {
            var personId = await PersonIdAsync(db, user);
            if (personId is null)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            try
            {
                await subs.WithdrawAsync(requestId, personId.Value);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            return Results.Redirect("/instructor/swaps");
        });

        group.MapPost("/set-attendance", async ([FromForm] Guid recordId, [FromForm] Guid sessionId, [FromForm] string status, AttendanceService attendance) =>
        {
            var target = status == "removed" ? AttendanceStatus.Removed : AttendanceStatus.Confirmed;
            try
            {
                await attendance.SetStatusAsync(recordId, target);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/instructor/roll/{sessionId}?failed=1");
            }

            return Results.Redirect($"/instructor/roll/{sessionId}");
        });

        group.MapPost("/confirm-all", async ([FromForm] Guid sessionId, AttendanceService attendance) =>
        {
            await attendance.ConfirmAllPendingAsync(sessionId);
            return Results.Redirect($"/instructor/roll/{sessionId}");
        });

        group.MapPost("/add-attendance", async ([FromForm] Guid sessionId, [FromForm] Guid personId, ClaimsPrincipal user, AttendanceService attendance) =>
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(raw, out var userId))
            {
                return Results.Redirect($"/instructor/roll/{sessionId}?failed=1");
            }

            try
            {
                await attendance.CheckInAsync(sessionId, personId, CheckInSource.Instructor, userId);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/instructor/roll/{sessionId}?failed=1");
            }

            return Results.Redirect($"/instructor/roll/{sessionId}");
        });

        return app;
    }

    private static async Task<Guid?> PersonIdAsync(GymStationDbContext db, ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId))
        {
            return null;
        }

        return (await db.Persons.SingleOrDefaultAsync(p => p.UserId == userId && !p.Archived))?.Id;
    }
}
