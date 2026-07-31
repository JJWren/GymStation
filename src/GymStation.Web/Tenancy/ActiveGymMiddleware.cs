using System.Security.Claims;
using GymStation.Infrastructure.Tenancy;

namespace GymStation.Web.Tenancy;

/// <summary>Hydrates the scoped TenantContext from the signed-in user's active-gym claim.</summary>
public class ActiveGymMiddleware(RequestDelegate next)
{
    public const string ActiveGymClaim = "gymstation:active_gym";

    public async Task InvokeAsync(HttpContext http, TenantContext tenant)
    {
        var raw = http.User.FindFirstValue(ActiveGymClaim);
        if (Guid.TryParse(raw, out var gymId))
        {
            tenant.SetGym(gymId);
        }

        await next(http);
    }
}
