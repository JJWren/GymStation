using GymStation.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.People;

/// <summary>Staff edits to a Person's own record — the invariants live here, not in pages.</summary>
public class PersonService(GymStationDbContext db)
{
    // Every name-writing path shares one rule set: required, trimmed, and inside
    // the 80-char columns — a friendlier failure than the database's.
    private static (string First, string Last) NormalizeName(string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new InvalidOperationException("First and last name are required.");
        }

        var first = firstName.Trim();
        var last = lastName.Trim();
        if (first.Length > 80 || last.Length > 80)
        {
            throw new InvalidOperationException("Names are 80 characters max.");
        }

        return (first, last);
    }

    public async Task UpdateAsync(
        Guid personId, string firstName, string lastName, DateOnly? dateOfBirth, PersonRoles roles,
        bool visitor, CancellationToken ct = default)
    {
        var (first, last) = NormalizeName(firstName, lastName);

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

        // Staff grants zero portal surface — a Staff-ONLY person with a login would
        // still reach the member shell (its pages gate on authentication, not roles).
        // Keep the invariant here, not in pages (#87).
        if (roles == PersonRoles.Staff && person.UserId is not null)
        {
            throw new InvalidOperationException(
                "Staff-only can't keep a login — the Staff role grants no portal. Add another role or unlink the login first.");
        }

        person.FirstName = first;
        person.LastName = last;
        person.DateOfBirth = dateOfBirth;
        person.Roles = roles;
        person.Visitor = visitor;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>#128's one-submit path: roles/DOB/visitor and contact persist together
    /// or not at all — a contact rejection must not strand a half-saved person.</summary>
    public async Task UpdateWithContactAsync(
        Guid personId, DateOnly? dateOfBirth, PersonRoles roles, bool visitor,
        string? phoneNumber, bool smsAllowed, CancellationToken ct = default)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await UpdateAsync(personId, person.FirstName, person.LastName, dateOfBirth, roles, visitor, ct);
            await SetContactAsync(personId, phoneNumber, smsAllowed, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            // The rollback undoes the rows, but the tracker still holds the flushed
            // mutations as "saved" — a retry on this context would silently no-op.
            db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>Renames a Person. Names edit inline at the page title (#128); every
    /// other field still flows through <see cref="UpdateAsync"/>.</summary>
    public async Task SetNameAsync(Guid personId, string firstName, string lastName, CancellationToken ct = default)
    {
        var (first, last) = NormalizeName(firstName, lastName);

        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        person.FirstName = first;
        person.LastName = last;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sets or clears a Person's contact number and text consent. Clearing the
    /// number always clears consent with it — there is nothing left to consent to.</summary>
    public async Task SetContactAsync(Guid personId, string? phoneNumber, bool smsAllowed, CancellationToken ct = default)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        var trimmed = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        if (trimmed is { Length: > 30 })
        {
            throw new InvalidOperationException("Phone numbers are 30 characters max.");
        }

        person.PhoneNumber = trimmed;
        person.SmsAllowed = trimmed is not null && smsAllowed;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Quick-add from the live roll: a walk-in with a name and nothing else.</summary>
    public async Task<Person> AddVisitorAsync(string firstName, string lastName, CancellationToken ct = default)
    {
        var (first, last) = NormalizeName(firstName, lastName);

        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = first,
            LastName = last,
            Roles = PersonRoles.Member,
            Visitor = true,
            JoinedOn = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);
        return person;
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
