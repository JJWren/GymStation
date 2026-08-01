using System.Security.Claims;
using GymStation.Domain.Attendance;
using GymStation.Domain.Events;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Attendance;
using GymStation.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Member;

public static class MemberActionEndpoints
{
    public static IEndpointRouteBuilder MapMemberActionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/member/actions")
            .RequireAuthorization()
            .ValidateAntiforgery();

        group.MapPost("/check-in", async (
            [FromForm] Guid sessionId,
            [FromForm] Guid personId,
            [FromForm] string? date,
            ClaimsPrincipal user,
            GymStationDbContext db,
            AttendanceService attendance) =>
        {
            var back = string.IsNullOrWhiteSpace(date) || !DateOnly.TryParse(date, out var day)
                ? "/schedule"
                : $"/schedule?date={day:yyyy-MM-dd}";
            var fail = back.Contains('?') ? $"{back}&failed=1" : $"{back}?failed=1";

            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(raw, out var userId))
            {
                return Results.Redirect(fail);
            }

            // Source is derived server-side: own record → Self; guardian-linked → Guardian.
            var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId && !p.Archived);
            var source = person?.UserId == userId
                ? CheckInSource.Self
                : CheckInSource.Guardian;

            try
            {
                await attendance.CheckInAsync(sessionId, personId, source, userId);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect(fail);
            }

            return Results.Redirect(back);
        });

        group.MapPost("/rsvp", async (
            [FromForm] Guid eventId,
            [FromForm] string status,
            ClaimsPrincipal user,
            GymStationDbContext db) =>
        {
            RsvpStatus? target = status switch
            {
                "going" => RsvpStatus.Going,
                "interested" => RsvpStatus.Interested,
                _ => null,
            };

            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (target is null || !Guid.TryParse(raw, out var userId))
            {
                return Results.Redirect("/events");
            }

            var me = await db.Persons.SingleOrDefaultAsync(p => p.UserId == userId && !p.Archived);
            if (me is null || !await db.GymEvents.AnyAsync(e => e.Id == eventId))
            {
                return Results.Redirect("/events");
            }

            var existing = await db.EventRsvps.SingleOrDefaultAsync(r => r.EventId == eventId && r.PersonId == me.Id);
            if (existing is null)
            {
                db.EventRsvps.Add(new EventRsvp { Id = Guid.NewGuid(), EventId = eventId, PersonId = me.Id, Status = target.Value });
            }
            else if (existing.Status == target.Value)
            {
                // Same button again = un-RSVP.
                db.EventRsvps.Remove(existing);
            }
            else
            {
                existing.Status = target.Value;
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Double-submit raced the unique (EventId, PersonId) index — the earlier
                // request already recorded the RSVP; treat as a no-op.
            }

            return Results.Redirect("/events");
        });

        return app;
    }
}
