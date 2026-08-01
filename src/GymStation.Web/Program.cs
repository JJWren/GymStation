using GymStation.Infrastructure;
using GymStation.Infrastructure.Identity;
using GymStation.Web.Admin;
using GymStation.Web.Auth;
using GymStation.Web.Components;
using GymStation.Web.Instructor;
using GymStation.Web.Ops;
using GymStation.Web.Tenancy;
using Microsoft.AspNetCore.Identity;

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

builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
    })
    .AddEntityFrameworkStores<GymStationDbContext>()
    .AddSignInManager();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/login";
    o.AccessDeniedPath = "/login";
});
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("GymStaff", p => p.RequireAuthenticatedUser().AddRequirements(new GymStaffRequirement()));
    o.AddPolicy("GymInstructor", p => p.RequireAuthenticatedUser().AddRequirements(new GymInstructorRequirement()));
});
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, GymStaffHandler>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, GymInstructorHandler>();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

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
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAuthEndpoints();
app.MapOpsEndpoints();
app.MapAdminActionEndpoints();
app.MapInstructorActionEndpoints();

app.Run();
