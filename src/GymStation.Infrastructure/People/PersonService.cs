using GymStation.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.People;

/// <summary>Staff edits to a Person's own record — the invariants live here, not in pages.</summary>
public class PersonService(GymStationDbContext db)
{
    public async Task UpdateAsync(
        Guid personId, string firstName, string lastName, DateOnly? dateOfBirth, PersonRoles roles,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new InvalidOperationException("First and last name are required.");
        }

        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        if (roles == PersonRoles.None)
        {
            roles = PersonRoles.Member;
        }

        if (person.Roles.HasFlag(PersonRoles.Owner) && !roles.HasFlag(PersonRoles.Owner))
        {
            await EnsureAnotherActiveOwnerAsync(personId, ct);
        }

        person.FirstName = firstName.Trim();
        person.LastName = lastName.Trim();
        person.DateOfBirth = dateOfBirth;
        person.Roles = roles;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetArchivedAsync(Guid personId, bool archived, CancellationToken ct = default)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        if (archived && !person.Archived && person.Roles.HasFlag(PersonRoles.Owner))
        {
            await EnsureAnotherActiveOwnerAsync(personId, ct);
        }

        person.Archived = archived;
        await db.SaveChangesAsync(ct);
    }

    // The gym must never lose its last active Owner — that account runs the place.
    private async Task EnsureAnotherActiveOwnerAsync(Guid personId, CancellationToken ct)
    {
        var anotherOwner = await db.Persons.AnyAsync(
            p => p.Id != personId && !p.Archived && p.Roles.HasFlag(PersonRoles.Owner), ct);
        if (!anotherOwner)
        {
            throw new InvalidOperationException(
                "This is the gym's only active Owner — give someone else the Owner role first.");
        }
    }
}
