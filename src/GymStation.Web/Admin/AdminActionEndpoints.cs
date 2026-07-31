using GymStation.Domain.Tenancy;
using GymStation.Infrastructure;
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

        return app;
    }
}
