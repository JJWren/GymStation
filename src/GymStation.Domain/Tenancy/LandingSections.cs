namespace GymStation.Domain.Tenancy;

/// <summary>
/// The public page's orderable sections (#134). The stored value is a comma list
/// of these keys; hero and foot are NOT keys — they bracket the page immutably,
/// and Contact rides with visit by decision.
/// </summary>
public static class LandingSections
{
    public const string About = "about";
    public const string Programs = "programs";
    public const string Schedule = "schedule";
    public const string Instructors = "instructors";
    public const string Stories = "stories";
    public const string Visit = "visit";

    /// <summary>The classic funnel — About first per the grilled decision.</summary>
    public static readonly IReadOnlyList<string> Default =
        [About, Programs, Schedule, Instructors, Stories, Visit];

    /// <summary>Parses a stored order into a full, valid permutation: unknown keys
    /// drop, duplicates collapse, missing keys append in default order. Never throws —
    /// stored junk degrades to something renderable.</summary>
    public static List<string> Normalize(string? stored)
    {
        var seen = new List<string>();
        foreach (var raw in (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = raw.ToLowerInvariant();
            if (Default.Contains(key) && !seen.Contains(key))
            {
                seen.Add(key);
            }
        }

        seen.AddRange(Default.Where(k => !seen.Contains(k)));
        return seen;
    }

    /// <summary>Moves a key one step up (-1) or down (+1); edges clamp silently.</summary>
    public static string Move(string? stored, string key, int direction)
    {
        var order = Normalize(stored);
        var index = order.IndexOf(key.ToLowerInvariant());
        if (index < 0 || direction is not (-1 or 1))
        {
            return string.Join(',', order);
        }

        var target = Math.Clamp(index + direction, 0, order.Count - 1);
        (order[index], order[target]) = (order[target], order[index]);
        return string.Join(',', order);
    }
}
