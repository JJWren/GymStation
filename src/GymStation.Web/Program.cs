using GymStation.Infrastructure;
using GymStation.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using GymStation.Web.Admin;
using GymStation.Web.Auth;
using GymStation.Web.Components;
using GymStation.Web.Instructor;
using GymStation.Web.Member;
using GymStation.Web.Ops;
using GymStation.Web.PublicActions;
using GymStation.Web.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddGymStationData(
    builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Default"),
    builder.Configuration["Storage:Root"] ?? "data/media");
builder.Services.AddGymStationEmail(new GymStation.Infrastructure.Notifications.EmailOptions(
    builder.Configuration["Email:Host"],
    int.TryParse(builder.Configuration["Email:Port"], out var smtpPort) ? smtpPort : 587,
    builder.Configuration["Email:Username"],
    builder.Configuration["Email:Password"],
    builder.Configuration["Email:From"] ?? "gymstation@localhost"));
builder.Services.AddGymStationWorkers();

// Contact-form rate limit (#138): 3 submissions per IP per hour. Rejections
// return 429 with no body — bots get nothing to learn from.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Enforced-empty rejection: bots get a bare 429 and nothing else.
    o.OnRejected = (_, _) => ValueTask.CompletedTask;
    o.AddPolicy("contact", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
    })
    .AddEntityFrameworkStores<GymStationDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<GymClaimsPrincipalFactory>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/login";
    // NOT /login: bouncing a signed-in member to the login page reads as being logged out.
    o.AccessDeniedPath = "/denied";
});
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("GymStaff", p => p.RequireAuthenticatedUser().AddRequirements(new GymStaffRequirement()));
    o.AddPolicy("GymInstructor", p => p.RequireAuthenticatedUser().AddRequirements(new GymInstructorRequirement()));
    o.AddPolicy("GymOwner", p => p.RequireAuthenticatedUser().AddRequirements(new GymOwnerRequirement()));
    // One policy per owner-grantable capability (#217): "Cap:ManageRanks" etc.
    foreach (var capability in Enum.GetValues<GymStation.Domain.People.GymCapability>())
    {
        o.AddPolicy(CapabilityRequirement.PolicyName(capability),
            p => p.RequireAuthenticatedUser().AddRequirements(new CapabilityRequirement(capability)));
    }
});
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, GymStaffHandler>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, GymInstructorHandler>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, GymOwnerHandler>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, CapabilityHandler>();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// TEST STACKS ONLY (round 4.5): the lab migrates by verified SQL script, never
// at boot — this flag exists so a disposable stack can rebuild itself from an
// empty database (schema always matches the running image). Default false;
// only the gymstation-test compose sets it.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStart"))
{
    using var migrationScope = app.Services.CreateScope();
    await migrationScope.ServiceProvider.GetRequiredService<GymStation.Infrastructure.GymStationDbContext>().Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
// Tenant hydration must precede authorization: the GymStaff policy reads the active gym.
app.UseMiddleware<ActiveGymMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAuthEndpoints();
app.MapOpsEndpoints();
app.MapAdminActionEndpoints();
app.MapFamilyActionEndpoints();
app.MapFamilyMemberEndpoints();
app.MapInstructorActionEndpoints();
app.MapMemberActionEndpoints();
app.MapProgramEndpoints();
app.MapStoryEndpoints();
app.MapRankSystemEndpoints();
app.MapContactEndpoints();
app.MapMediaEndpoints();

app.Run();
