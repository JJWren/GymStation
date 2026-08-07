using GymStation.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.People;

/// <summary>
/// Owner-managed capability grants (#217). Grants attach to any staff-ish
/// Person — Admins AND Instructors (a head coach can hold ManageRanks without
/// the Admin role). Owners are implicitly all-capable and never carry rows;
/// authorization enforcement lives in the capability policy handler.
/// </summary>
public class PermissionService(GymStationDbContext db)
{
    private const PersonRoles StaffishRoles = PersonRoles.Owner | PersonRoles.Admin | PersonRoles.Instructor;

    public async Task<HashSet<GymCapability>> GetForPersonAsync(Guid personId, CancellationToken ct = default)
    {
        return [.. await db.PermissionGrants.Where(g => g.PersonId == personId).Select(g => g.Capability).ToListAsync(ct)];
    }

    /// <summary>Capabilities of the signed-in user's Person in the active gym.
    /// Owners report the full set — callers never special-case them.</summary>
    public async Task<HashSet<GymCapability>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.UserId == userId && !p.Archived, ct);
        if (person is null)
        {
            return [];
        }

        if (person.HasRole(PersonRoles.Owner))
        {
            return [.. Enum.GetValues<GymCapability>()];
        }

        return await GetForPersonAsync(person.Id, ct);
    }

    /// <summary>Replaces a Person's grant set. Owner-gating is the endpoint's job.</summary>
    public async Task SetForPersonAsync(Guid personId, IReadOnlyCollection<GymCapability> capabilities, CancellationToken ct = default)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId && !p.Archived, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        if ((person.Roles & StaffishRoles) == 0)
        {
            throw new InvalidOperationException("Capabilities attach to staff-ish people — make them Admin or Instructor first.");
        }

        if (person.HasRole(PersonRoles.Owner))
        {
            throw new InvalidOperationException("Owners hold every capability implicitly — there is nothing to grant.");
        }

        var gymId = db.CurrentGymId
            ?? throw new InvalidOperationException("No active gym.");

        var wanted = capabilities.Where(c => Enum.IsDefined(c)).Distinct().ToHashSet();
        var existing = await db.PermissionGrants.Where(g => g.PersonId == personId).ToListAsync(ct);

        db.PermissionGrants.RemoveRange(existing.Where(g => !wanted.Contains(g.Capability)));
        foreach (var capability in wanted.Except(existing.Select(g => g.Capability)))
        {
            db.PermissionGrants.Add(new PermissionGrant
            {
                Id = Guid.NewGuid(),
                GymId = gymId,
                PersonId = personId,
                Capability = capability,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public Task ApplyPresetAsync(Guid personId, string preset, CancellationToken ct = default)
    {
        return CapabilityPresets.All.TryGetValue(preset, out var capabilities)
            ? SetForPersonAsync(personId, capabilities, ct)
            : throw new InvalidOperationException("Unknown permission preset.");
    }
}
