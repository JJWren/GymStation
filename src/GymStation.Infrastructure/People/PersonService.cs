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

    /// <summary>
    /// Post-hoc login link (#191) — the create-time-only "Link login by email"
    /// finally gets an editing life. The caller resolves the email to a User id
    /// (logins are global; UserManager lives at the edge).
    /// </summary>
    public async Task LinkLoginAsync(Guid personId, Guid userId, CancellationToken ct = default)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId && !p.Archived, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        if (person.UserId is not null)
        {
            throw new InvalidOperationException("That person already holds a login — unlink it first.");
        }

        if (person.Roles == PersonRoles.Staff)
        {
            throw new InvalidOperationException("Staff-only can't hold a login — the Staff role grants no portal. Add another role first.");
        }

        // #92 wording, same (GymId, UserId) space as graduation's taken-email guard.
        if (await db.Persons.AnyAsync(p => p.UserId == userId, ct))
        {
            throw new InvalidOperationException("That email already belongs to another member of this gym — use a different one.");
        }

        person.UserId = userId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Clears the link (#191). The User keeps its account and any
    /// guardianship — but a primary guardian's family billing skips until a
    /// roster Person is linked again.</summary>
    public async Task UnlinkLoginAsync(Guid personId, CancellationToken ct = default)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        person.UserId = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Assigns (or clears) a Person's individual plan (#196) — the long-missing
    /// mirror of SetFamilyPlanAsync's guard: only unarchived PER-PERSON plans
    /// attach here (a family-scope plan on a person would bill its flat base and
    /// ignore #181 sizing). Assigning a real plan CONVERTS a visitor — the
    /// documented conversion moment; clearing a plan never re-flags.
    /// </summary>
    public async Task AssignPlanAsync(Guid personId, Guid? planId, CancellationToken ct = default)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        if (planId is { } id)
        {
            var plan = await db.MembershipPlans.SingleOrDefaultAsync(pl => pl.Id == id && !pl.Archived, ct)
                ?? throw new InvalidOperationException("Plan not found in the active gym.");
            if (plan.Scope != Domain.Money.PlanScope.PerPerson)
            {
                throw new InvalidOperationException("Family plans attach to a FAMILY, not a person — set it on their family page.");
            }

            person.Visitor = false;
        }

        person.MembershipPlanId = planId;
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
