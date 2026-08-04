using System.Globalization;
using GymStation.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Notifications;

/// <summary>
/// The inbox filter state (#182): read chips (unread is the default view),
/// title search, and the ALL/DAY/RANGE date modes. Resolved once from raw
/// query params, then applied as predicates — the shared panel, its pager
/// links, and the scoped mark-read all speak this one shape.
/// </summary>
public sealed record NotificationFilter(string Read, string? Query, string DateMode, DateOnly? On, DateOnly? From, DateOnly? To)
{
    /// <summary>True when any control narrows past the default view (UNREAD, no
    /// search, all dates) — drives the MARK ALL READ / MARK THESE READ label.</summary>
    public bool Narrowed => Read != "unread" || Query is not null || DateMode != "all";
}

public static class NotificationFilters
{
    /// <summary>
    /// Normalizes raw query params: unknown read values fall back to unread,
    /// dates parse ISO-invariant only, a reversed range swaps, out-of-range
    /// dates clamp to [2000-01-01, today], and a date mode missing a valid
    /// date falls back to ALL.
    /// </summary>
    public static NotificationFilter Resolve(
        string? read, string? q, string? dmode, string? onRaw, string? fromRaw, string? toRaw, DateOnly today)
    {
        var readState = read is "read" or "all" ? read : "unread";
        var query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var min = new DateOnly(2000, 1, 1);
        DateOnly Clamp(DateOnly d) => d < min ? min : d > today ? today : d;

        if (dmode == "day" && TryParseDate(onRaw, out var on))
        {
            return new(readState, query, "day", Clamp(on), null, null);
        }

        if (dmode == "range" && TryParseDate(fromRaw, out var from) && TryParseDate(toRaw, out var to))
        {
            if (to < from)
            {
                (from, to) = (to, from);
            }

            return new(readState, query, "range", null, Clamp(from), Clamp(to));
        }

        return new(readState, query, "all", null, null, null);
    }

    private static bool TryParseDate(string? raw, out DateOnly date) =>
        DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>
    /// Applies the filter to a user's notification scope. Date windows are
    /// GYM-LOCAL days converted to UTC instants against CreatedUtc — inclusive
    /// start, exclusive next-day end, so both range endpoints are whole days.
    /// </summary>
    public static IQueryable<Notification> Apply(IQueryable<Notification> scope, NotificationFilter filter, TimeZoneInfo zone)
    {
        scope = filter.Read switch
        {
            "read" => scope.Where(n => n.ReadUtc != null),
            "all" => scope,
            _ => scope.Where(n => n.ReadUtc == null),
        };

        if (filter.Query is { } q)
        {
            // Postgres ILIKE: culture-safe case-insensitivity; escape LIKE wildcards
            // in user input. The escape char must be declared — the two-arg ILike
            // translation applies NO escape, leaving backslashes literal.
            var pattern = "%" + q
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_") + "%";
            scope = scope.Where(n => EF.Functions.ILike(n.Title, pattern, @"\"));
        }

        var (from, to) = filter.DateMode switch
        {
            "day" => (filter.On, filter.On),
            "range" => (filter.From, filter.To),
            _ => ((DateOnly?)null, (DateOnly?)null),
        };

        if (from is { } first && to is { } last)
        {
            var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(first.ToDateTime(TimeOnly.MinValue), zone));
            var endUtcExclusive = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(last.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
            scope = scope.Where(n => n.CreatedUtc >= startUtc && n.CreatedUtc < endUtcExclusive);
        }

        return scope;
    }
}
