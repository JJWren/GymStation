using System.Net.Mail;
using GymStation.Domain.Contact;
using GymStation.Domain.Notifications;
using GymStation.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymStation.Infrastructure.Contact;

/// <summary>Best-effort MX probe — injectable so tests never touch DNS.</summary>
public interface IMxLookup
{
    /// <summary>True when the domain plausibly receives mail, and ALSO true on
    /// timeout or resolver error — the home-lab DNS lies sometimes, and a flaky
    /// resolver must never cost the gym a walk-in lead (fail-open by decision).</summary>
    Task<bool> ProbablyAcceptsMailAsync(string domain, CancellationToken ct = default);
}

public sealed class DnsMxLookup : IMxLookup
{
    public async Task<bool> ProbablyAcceptsMailAsync(string domain, CancellationToken ct = default)
    {
        try
        {
            var lookup = new DnsClient.LookupClient(new DnsClient.LookupClientOptions
            {
                Timeout = TimeSpan.FromMilliseconds(1500),
                Retries = 0,
                UseCache = true,
            });
            var response = await lookup.QueryAsync(domain, DnsClient.QueryType.MX, cancellationToken: ct);
            if (response.HasError)
            {
                return true; // resolver trouble — fail open
            }

            if (response.Answers.MxRecords().Any())
            {
                return true;
            }

            // No MX: many small domains receive on their A record — one more probe.
            var a = await lookup.QueryAsync(domain, DnsClient.QueryType.A, cancellationToken: ct);
            return a.HasError || a.Answers.ARecords().Any();
        }
        catch
        {
            return true; // timeout/any failure — fail open
        }
    }
}

public enum ContactOutcome
{
    Accepted = 0,

    /// <summary>The honeypot tripped: pretend success, store nothing.</summary>
    SilentDrop = 1,

    /// <summary>Validation or the spam wall said no — generic public message.</summary>
    Rejected = 2,
}

/// <summary>The public contact form's whole pipeline (#138): layered passive
/// spam wall, then store + fan out. Tenant must already be set by the caller.</summary>
public class ContactService(
    GymStationDbContext db,
    NotificationService notifications,
    IEmailDeliverer email,
    IMxLookup mx,
    ILogger<ContactService> logger)
{
    // Sell-you-something vocabulary; body must also stay under the link cap.
    private static readonly string[] SellerPhrases =
    [
        "seo", "backlink", "guest post", "crypto", "casino", "viagra", "loan offer",
        "marketing services", "increase your traffic", "web design services", "lead generation",
    ];

    public async Task<ContactOutcome> SubmitAsync(
        string? honeypot, TimeSpan formAge,
        string? firstName, string? lastName, string? email_, string? phone, string? body,
        CancellationToken ct = default)
    {
        // 1) Honeypot: bots fill everything. Pretend success; store nothing.
        if (!string.IsNullOrWhiteSpace(honeypot))
        {
            return ContactOutcome.SilentDrop;
        }

        // 2) Minimum time-to-submit: humans read before they write.
        if (formAge < TimeSpan.FromSeconds(3) || formAge > TimeSpan.FromHours(24))
        {
            return ContactOutcome.Rejected;
        }

        var first = (firstName ?? "").Trim();
        var last = (lastName ?? "").Trim();
        var text = (body ?? "").Trim();
        if (first is { Length: 0 or > 80 } || last is { Length: 0 or > 80 } || text.Length is < 10 or > 2000)
        {
            return ContactOutcome.Rejected;
        }

        // 3) Content heuristics: link cap + the seller vocabulary.
        var lowered = text.ToLowerInvariant();
        var linkCount = CountOf(lowered, "http://") + CountOf(lowered, "https://") + CountOf(lowered, "www.");
        if (linkCount > 2 || SellerPhrases.Any(lowered.Contains))
        {
            return ContactOutcome.Rejected;
        }

        // 4) Email or phone — at least one, both fine.
        var cleanEmail = (email_ ?? "").Trim();
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (cleanEmail.Length == 0 && digits.Length == 0)
        {
            return ContactOutcome.Rejected;
        }

        if (digits.Length is > 0 and (< 7 or > 15))
        {
            return ContactOutcome.Rejected;
        }

        if (cleanEmail.Length > 0)
        {
            if (cleanEmail.Length > 200
                || !MailAddress.TryCreate(cleanEmail, out var parsed)
                || !parsed.Host.Contains('.'))
            {
                return ContactOutcome.Rejected;
            }

            // 5) Best-effort MX (fail-open inside the lookup).
            if (!await mx.ProbablyAcceptsMailAsync(parsed.Host, ct))
            {
                return ContactOutcome.Rejected;
            }
        }

        var message = new ContactMessage
        {
            Id = Guid.NewGuid(),
            FirstName = first,
            LastName = last,
            Email = cleanEmail.Length == 0 ? null : cleanEmail,
            Phone = digits.Length == 0 ? null : digits,
            Body = text,
        };
        db.ContactMessages.Add(message);

        // In-app heads-up for admins (no per-user email — the forward address
        // covers email needs); rides this same SaveChanges transactionally.
        var admins = await notifications.StaffUserIdsAsync(ct);
        notifications.Notify(
            admins,
            NotificationCategory.ContactMessageReceived,
            $"New contact message from {first} {last}",
            text.Length > 140 ? text[..140] + "…" : text,
            "/admin/messages",
            email: false);

        await db.SaveChangesAsync(ct);

        // Optional forward — after the save, and never at the lead's expense.
        var settings = await db.GymSettings.SingleOrDefaultAsync(ct);
        if (settings?.ContactForwardEmail is { Length: > 0 } forward)
        {
            try
            {
                var reach = string.Join(" · ", new[] { message.Email, FormatPhone(message.Phone) }.Where(v => v is not null));
                await email.SendAsync(forward, $"Contact form: {first} {last}", $"{reach}\n\n{text}", ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Contact-form forward to {Forward} failed; the message is stored regardless.", forward);
            }
        }

        return ContactOutcome.Accepted;
    }

    /// <summary>Digits → "(###) ###-####" for US-length numbers; others pass through.</summary>
    public static string? FormatPhone(string? digits)
        => digits is { Length: 10 } ? $"({digits[..3]}) {digits[3..6]}-{digits[6..]}" : digits;

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
