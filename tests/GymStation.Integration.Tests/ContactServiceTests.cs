using GymStation.Domain.Notifications;
using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Contact;
using GymStation.Infrastructure.Notifications;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class ContactServiceTests(PostgresFixture fixture)
{
    private sealed class FakeMx(bool result) : IMxLookup
    {
        public Task<bool> ProbablyAcceptsMailAsync(string domain, CancellationToken ct = default) => Task.FromResult(result);
    }

    private sealed class RecordingEmail : IEmailDeliverer
    {
        public readonly List<(string To, string Subject)> Sent = [];
        public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
        {
            Sent.Add((toEmail, subject));
            return Task.CompletedTask;
        }
    }

    private static readonly TimeSpan HumanAge = TimeSpan.FromSeconds(20);

    private async Task<(TenantContext Tenant, Guid AdminUserId)> SeedGymAsync(string? forward = null)
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Ct {suffix}", Slug = $"ct-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);

        await using var context = fixture.CreateContext(tenant);
        context.GymSettings.Add(new GymSettings { GymId = gym.Id, ContactForwardEmail = forward });
        var adminUser = Guid.NewGuid();
        context.Persons.Add(new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Jordan",
            LastName = "Torres",
            Roles = PersonRoles.Owner | PersonRoles.Admin,
            UserId = adminUser,
            JoinedOn = new DateOnly(2026, 1, 1),
        });
        await context.SaveChangesAsync();
        return (tenant, adminUser);
    }

    private ContactService Make(GymStationDbContext context, bool mxResult = true, RecordingEmail? email = null)
        => new(context, new NotificationService(context), email ?? new RecordingEmail(), new FakeMx(mxResult), NullLogger<ContactService>.Instance);

    [Fact]
    public async Task HappyPath_StoresNormalizesNotifies_AndForwards()
    {
        var (tenant, adminUser) = await SeedGymAsync(forward: "frontdesk@example.test");
        await using var context = fixture.CreateContext(tenant);
        var email = new RecordingEmail();
        var service = Make(context, email: email);

        var outcome = await service.SubmitAsync(
            honeypot: null, formAge: HumanAge,
            "Casey", "Nguyen", "casey@example.test", "(251) 555-0142", "Interested in Muay Thai for two adults.");

        Assert.Equal(ContactOutcome.Accepted, outcome);
        var stored = await context.ContactMessages.SingleAsync();
        Assert.Equal("2515550142", stored.Phone); // digits only — render pretty later
        Assert.Null(stored.ReadUtc);
        Assert.Contains(await context.Notifications.ToListAsync(),
            n => n.Category == NotificationCategory.ContactMessageReceived && n.RecipientUserId == adminUser && n.LinkPath == "/admin/messages");
        Assert.Single(email.Sent, s => s.To == "frontdesk@example.test");
    }

    [Fact]
    public async Task Honeypot_DropsSilently_NothingStored()
    {
        var (tenant, _) = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);

        var outcome = await Make(context).SubmitAsync(
            honeypot: "https://spam.example", formAge: HumanAge,
            "A", "Bot", "bot@example.test", null, "Buy my thing right now please.");

        Assert.Equal(ContactOutcome.SilentDrop, outcome);
        Assert.Empty(await context.ContactMessages.ToListAsync());
        Assert.Empty(await context.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData(1)]      // faster than any human
    [InlineData(90000)]  // a day-old stale form
    public async Task WrongAge_IsRejected(int seconds)
    {
        var (tenant, _) = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);

        var outcome = await Make(context).SubmitAsync(
            null, TimeSpan.FromSeconds(seconds),
            "Casey", "Nguyen", "casey@example.test", null, "Legitimate question about classes.");

        Assert.Equal(ContactOutcome.Rejected, outcome);
        Assert.Empty(await context.ContactMessages.ToListAsync());
    }

    [Fact]
    public async Task MissingBothEmailAndPhone_IsRejected()
    {
        var (tenant, _) = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);

        var outcome = await Make(context).SubmitAsync(
            null, HumanAge, "Casey", "Nguyen", null, "  ", "How much are drop-ins for visitors?");

        Assert.Equal(ContactOutcome.Rejected, outcome);
    }

    [Theory]
    [InlineData("We offer SEO and backlink packages for your site.")]
    [InlineData("visit http://a.example http://b.example https://c.example for deals")]
    public async Task SellerContent_IsRejected(string body)
    {
        var (tenant, _) = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);

        var outcome = await Make(context).SubmitAsync(
            null, HumanAge, "Sales", "Bot", "sales@example.test", null, body);

        Assert.Equal(ContactOutcome.Rejected, outcome);
    }

    [Fact]
    public async Task MxSaysNo_RejectsEmailOnlySubmission()
    {
        var (tenant, _) = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);

        var outcome = await Make(context, mxResult: false).SubmitAsync(
            null, HumanAge, "Casey", "Nguyen", "casey@no-such-mailbox.test", null, "Real question, fake mailbox domain.");

        Assert.Equal(ContactOutcome.Rejected, outcome);
    }

    [Fact]
    public void PhoneFormatting_RoundTripsUsNumbers()
    {
        Assert.Equal("(251) 555-0142", ContactService.FormatPhone("2515550142"));
        Assert.Equal("5550142", ContactService.FormatPhone("5550142")); // non-10 passes through
    }
}
