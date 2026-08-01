using Microsoft.AspNetCore.Antiforgery;

namespace GymStation.Web.Http;

public static class EndpointFilters
{
    /// <summary>
    /// Explicit antiforgery validation for form-post endpoint groups — covers endpoints
    /// that bind no form fields (which miss the framework's automatic validation).
    /// </summary>
    public static TBuilder ValidateAntiforgery<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var antiforgery = ctx.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(ctx.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("Invalid antiforgery token.");
            }

            return await next(ctx);
        });

        return builder;
    }
}
