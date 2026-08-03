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
            [FromForm] string? back,
            ClaimsPrincipal user,
            GymStationDbContext db,
            GymStation.Infrastructure.People.FamilyService families,
            [FromForm] Guid? personId = null) =>
        {
            // Allow-listed, never caller-controlled paths — no open redirect.
            var destination = back == "detail" ? $"/events/{eventId}" : "/events";

            RsvpStatus? target = status switch
            {
                "going" => RsvpStatus.Going,
                "interested" => RsvpStatus.Interested,
                _ => null,
            };

            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (target is null || !Guid.TryParse(raw, out var userId))
            {
                return Results.Redirect(destination);
            }

            // Whose RSVP: own Person by default; a ward's when the caller is an
            // ActForWards guardian for them (#90).
            Guid rsvpPersonId;
            if (personId is { } wardId)
            {
                var me = await db.Persons.SingleOrDefaultAsync(p => p.UserId == userId && !p.Archived);
                if (me?.Id != wardId && !await families.CanActForAsync(userId, wardId))
                {
                    return Results.Redirect(destination);
                }

                rsvpPersonId = wardId;
            }
            else
            {
                var me = await db.Persons.SingleOrDefaultAsync(p => p.UserId == userId && !p.Archived);
                if (me is null)
                {
                    return Results.Redirect(destination);
                }

                rsvpPersonId = me.Id;
            }

            if (!await db.GymEvents.AnyAsync(e => e.Id == eventId))
            {
                return Results.Redirect(destination);
            }

            var existing = await db.EventRsvps.SingleOrDefaultAsync(r => r.EventId == eventId && r.PersonId == rsvpPersonId);
            if (existing is null)
            {
                db.EventRsvps.Add(new EventRsvp { Id = Guid.NewGuid(), EventId = eventId, PersonId = rsvpPersonId, Status = target.Value });
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

            return Results.Redirect(destination);
        });

        return app;
    }
}
