using System.Security.Claims;
using GymStation.Domain.Attendance;
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
            var back = string.IsNullOrWhiteSpace(date) ? "/schedule" : $"/schedule?date={date}";
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(raw, out var userId))
            {
                return Results.Redirect($"{back}&failed=1");
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
                return Results.Redirect(back.Contains('?') ? $"{back}&failed=1" : $"{back}?failed=1");
            }

            return Results.Redirect(back);
        });

        return app;
    }
}
