using GymStation.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.People;

/// <summary>What graduation hands back for the one-time reveal (#92).</summary>
public sealed record GraduationResult(string PersonName, string Email, string? TempPassword);

/// <summary>Who is asking: a guardian login, or staff using the admin surface.
/// Staff are STRUCTURE-ONLY (decision 6): they pass every structural gate here but
/// never the acting gate — CanActForAsync ignores staff entirely.</summary>
public readonly record struct FamilyActor(Guid? UserId, bool IsStaff)
{
    public static FamilyActor Staff => new(null, true);
    public static FamilyActor User(Guid userId) => new(userId, false);
}

/// <summary>
/// The family aggregate's rules live here, never in pages (#89): membership, the
/// guardian permission matrix (ActForWards/ManageGuardians/ManageMembers/ViewBilling),
/// and the single transferable PRIMARY.
/// </summary>
public class FamilyService(GymStationDbContext db)
{
    // ---------- acting (the guardian-authority gate) ----------

    /// <summary>The wards this login may act for: check-in, RSVP, diary, progress.</summary>
    public async Task<List<Person>> WardsForAsync(Guid guardianUserId, CancellationToken ct = default)
    {
        return await db.FamilyGuardians
            .Where(g => g.GuardianUserId == guardianUserId && g.ActForWards)
            .Join(db.FamilyMembers.Where(m => m.IsWard), g => g.FamilyId, m => m.FamilyId, (g, m) => m.PersonId)
            .Join(db.Persons.Where(p => !p.Archived), pid => pid, p => p.Id, (pid, p) => p)
            .OrderBy(p => p.FirstName).ThenBy(p => p.LastName)
            .ToListAsync(ct);
    }

    /// <summary>True when the login is an ActForWards guardian of this Person's family
    /// and the Person is an unarchived ward — the same shape WardsForAsync lists, so
    /// acting authority and the switcher can never disagree. Staff never pass this
    /// gate — admins are structure-only.</summary>
    public async Task<bool> CanActForAsync(Guid guardianUserId, Guid personId, CancellationToken ct = default)
    {
        return await db.FamilyGuardians
            .Where(g => g.GuardianUserId == guardianUserId && g.ActForWards)
            .Join(db.FamilyMembers.Where(m => m.IsWard && m.PersonId == personId),
                g => g.FamilyId, m => m.FamilyId, (g, m) => m.PersonId)
            .Join(db.Persons.Where(p => !p.Archived), pid => pid, p => p.Id, (pid, p) => p.Id)
            .AnyAsync(ct);
    }

    /// <summary>Guardianship keeps a login attached to a gym only while an unarchived
    /// ward exists — the same semantics GymMembershipService and the claims factory
    /// apply on their unfiltered paths.</summary>
    public async Task<bool> IsGuardianInGymAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.FamilyGuardians
            .Where(g => g.GuardianUserId == userId)
            .Join(db.FamilyMembers.Where(m => m.IsWard), g => g.FamilyId, m => m.FamilyId, (g, m) => m.PersonId)
            .Join(db.Persons.Where(p => !p.Archived), pid => pid, p => p.Id, (pid, p) => p.Id)
            .AnyAsync(ct);
    }

    /// <summary>Families this login guards, fully loaded for the MY FAMILY surface.</summary>
    public async Task<List<Family>> FamiliesForGuardianAsync(Guid userId, CancellationToken ct = default)
    {
        var familyIds = await db.FamilyGuardians
            .Where(g => g.GuardianUserId == userId)
            .Select(g => g.FamilyId)
            .ToListAsync(ct);

        return await LoadedFamilies().Where(f => familyIds.Contains(f.Id)).OrderBy(f => f.Name).ToListAsync(ct);
    }

    // ---------- structure (admin or flag-gated guardian) ----------

    public async Task<Family> CreateFamilyAsync(FamilyActor actor, string name, CancellationToken ct = default)
    {
        if (!actor.IsStaff)
        {
            throw new InvalidOperationException("Only staff create families from scratch.");
        }

        var family = new Family { Id = Guid.NewGuid(), Name = NormalizeName(name) };
        db.Families.Add(family);
        await db.SaveChangesAsync(ct);
        return family;
    }

    public async Task RenameFamilyAsync(FamilyActor actor, Guid familyId, string name, CancellationToken ct = default)
    {
        var family = await RequireFamilyAsync(familyId, ct);
        await RequireAsync(actor, familyId, g => g.ManageMembers, "rename the family", ct);
        family.Name = NormalizeName(name);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddMemberAsync(FamilyActor actor, Guid familyId, Guid personId, bool isWard, CancellationToken ct = default)
    {
        _ = await RequireFamilyAsync(familyId, ct);
        await RequireAsync(actor, familyId, g => g.ManageMembers, "add family members", ct);

        _ = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        if (await db.FamilyMembers.AnyAsync(m => m.PersonId == personId, ct))
        {
            throw new InvalidOperationException("That person already belongs to a family — one family per person.");
        }

        db.FamilyMembers.Add(new FamilyMember { Id = Guid.NewGuid(), FamilyId = familyId, PersonId = personId, IsWard = isWard });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetWardAsync(FamilyActor actor, Guid familyId, Guid personId, bool isWard, CancellationToken ct = default)
    {
        await RequireAsync(actor, familyId, g => g.ManageMembers, "change ward status", ct);
        var member = await db.FamilyMembers.SingleOrDefaultAsync(m => m.FamilyId == familyId && m.PersonId == personId, ct)
            ?? throw new InvalidOperationException("That person isn't in this family.");
        member.IsWard = isWard;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveMemberAsync(FamilyActor actor, Guid familyId, Guid personId, CancellationToken ct = default)
    {
        await RequireAsync(actor, familyId, g => g.ManageMembers, "remove family members", ct);
        var member = await db.FamilyMembers.SingleOrDefaultAsync(m => m.FamilyId == familyId && m.PersonId == personId, ct)
            ?? throw new InvalidOperationException("That person isn't in this family.");
        db.FamilyMembers.Remove(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddGuardianAsync(
        FamilyActor actor, Guid familyId, Guid guardianUserId,
        bool actForWards = true, bool manageGuardians = false, bool manageMembers = false, bool viewBilling = false,
        CancellationToken ct = default)
    {
        var family = await RequireFamilyAsync(familyId, ct);
        await RequireAsync(actor, familyId, g => g.ManageGuardians, "add guardians", ct);

        if (await db.FamilyGuardians.AnyAsync(g => g.FamilyId == familyId && g.GuardianUserId == guardianUserId, ct))
        {
            throw new InvalidOperationException("That login is already a guardian of this family.");
        }

        // The FIRST guardian becomes primary and holds everything — someone must own the family.
        var isFirst = !await db.FamilyGuardians.AnyAsync(g => g.FamilyId == familyId, ct);
        db.FamilyGuardians.Add(new FamilyGuardian
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            GuardianUserId = guardianUserId,
            IsPrimary = isFirst,
            ActForWards = isFirst || actForWards,
            ManageGuardians = isFirst || manageGuardians,
            ManageMembers = isFirst || manageMembers,
            ViewBilling = isFirst || viewBilling,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetGuardianFlagsAsync(
        FamilyActor actor, Guid familyId, Guid guardianId,
        bool actForWards, bool manageGuardians, bool manageMembers, bool viewBilling,
        CancellationToken ct = default)
    {
        await RequireAsync(actor, familyId, g => g.ManageGuardians, "change guardian permissions", ct);
        var guardian = await RequireGuardianAsync(familyId, guardianId, ct);
        if (guardian.IsPrimary)
        {
            throw new InvalidOperationException("The primary guardian holds every permission — transfer primacy instead.");
        }

        guardian.ActForWards = actForWards;
        guardian.ManageGuardians = manageGuardians;
        guardian.ManageMembers = manageMembers;
        guardian.ViewBilling = viewBilling;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveGuardianAsync(FamilyActor actor, Guid familyId, Guid guardianId, CancellationToken ct = default)
    {
        await RequireAsync(actor, familyId, g => g.ManageGuardians, "remove guardians", ct);
        var guardian = await RequireGuardianAsync(familyId, guardianId, ct);
        if (guardian.IsPrimary)
        {
            throw new InvalidOperationException("The primary guardian can't be removed — transfer primacy first.");
        }

        db.FamilyGuardians.Remove(guardian);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Assigns (or clears) the family's plan — staff-only structure, like
    /// creation. The plan must be Family-scope and unarchived; the cycle then bills
    /// the primary's Person once per month (#91).</summary>
    public async Task SetFamilyPlanAsync(FamilyActor actor, Guid familyId, Guid? planId, CancellationToken ct = default)
    {
        if (!actor.IsStaff)
        {
            throw new InvalidOperationException("Only staff assign family plans.");
        }

        var family = await RequireFamilyAsync(familyId, ct);
        if (planId is { } id)
        {
            var plan = await db.MembershipPlans.SingleOrDefaultAsync(pl => pl.Id == id, ct)
                ?? throw new InvalidOperationException("Plan not found in the active gym.");
            if (plan.Archived || plan.Scope != Domain.Money.PlanScope.Family || plan.Cadence != Domain.Money.PlanCadence.Monthly)
            {
                throw new InvalidOperationException("Family plans must be active, monthly, family-scope plans.");
            }
        }

        family.MembershipPlanId = planId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Graduation (#92): a ward becomes their own adult account, by the PRIMARY or
    /// staff. IsWard clears — which by itself re-privatizes the ENTIRE diary history
    /// to the new adult (guardian authority is derived from IsWard, so nothing to
    /// migrate) — and family membership continues as an adult, so a family plan may
    /// keep covering them. A ward without a login gets one created on the given email
    /// with a one-time password the caller shows them once; a ward who already has a
    /// login keeps it.
    /// </summary>
    public async Task<GraduationResult> GraduateAsync(
        FamilyActor actor, Guid familyId, Guid personId, string? email,
        Microsoft.AspNetCore.Identity.UserManager<Identity.AppUser> users, CancellationToken ct = default)
    {
        var current = await db.FamilyGuardians.SingleOrDefaultAsync(g => g.FamilyId == familyId && g.IsPrimary, ct);
        if (!actor.IsStaff && (current is null || actor.UserId != current.GuardianUserId))
        {
            throw new InvalidOperationException("Only the primary guardian (or staff) can graduate a ward.");
        }

        var member = await db.FamilyMembers.SingleOrDefaultAsync(m => m.FamilyId == familyId && m.PersonId == personId, ct)
            ?? throw new InvalidOperationException("That person isn't in this family.");
        if (!member.IsWard)
        {
            throw new InvalidOperationException("That person is already an adult member.");
        }

        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId && !p.Archived, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        string? tempPassword = null;
        string loginEmail;
        if (person.UserId is null)
        {
            loginEmail = email?.Trim() ?? "";
            if (loginEmail.Length == 0)
            {
                throw new InvalidOperationException("Graduation needs an email for the new login.");
            }

            var existing = await users.FindByEmailAsync(loginEmail);
            if (existing is not null)
            {
                person.UserId = existing.Id;
            }
            else
            {
                // One-time password, shown once by the caller — the gym hands it over,
                // the same way every login starts here.
                tempPassword = $"{Guid.NewGuid():N}"[..10] + "Aa1!";
                var user = new Identity.AppUser
                {
                    Id = Guid.NewGuid(),
                    UserName = loginEmail,
                    Email = loginEmail,
                };
                var created = await users.CreateAsync(user, tempPassword);
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Couldn't create the login: {string.Join("; ", created.Errors.Select(e => e.Description))}");
                }

                person.UserId = user.Id;
            }
        }
        else
        {
            loginEmail = (await users.FindByIdAsync(person.UserId.Value.ToString()))?.Email ?? "their existing login";
        }

        member.IsWard = false;
        await db.SaveChangesAsync(ct);
        return new GraduationResult(person.DisplayName, loginEmail, tempPassword);
    }

    /// <summary>Primacy moves as one atomic step: only the current primary (or staff)
    /// may hand it over; the new primary gains every flag.</summary>
    public async Task TransferPrimaryAsync(FamilyActor actor, Guid familyId, Guid toGuardianId, CancellationToken ct = default)
    {
        var current = await db.FamilyGuardians.SingleOrDefaultAsync(g => g.FamilyId == familyId && g.IsPrimary, ct)
            ?? throw new InvalidOperationException("This family has no primary guardian — repair it from the admin surface.");

        if (!actor.IsStaff && actor.UserId != current.GuardianUserId)
        {
            throw new InvalidOperationException("Only the current primary guardian (or staff) can transfer primacy.");
        }

        var target = await RequireGuardianAsync(familyId, toGuardianId, ct);
        if (target.Id == current.Id)
        {
            return;
        }

        // Two saves inside one transaction: the filtered one-primary index checks per
        // statement, so the old primary must clear before the new one sets.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        current.IsPrimary = false;
        await db.SaveChangesAsync(ct);

        target.IsPrimary = true;
        target.ActForWards = true;
        target.ManageGuardians = true;
        target.ManageMembers = true;
        target.ViewBilling = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ---------- internals ----------

    private IQueryable<Family> LoadedFamilies() => db.Families
        .Include(f => f.Members).ThenInclude(m => m.Person)
        .Include(f => f.Guardians);

    private async Task<Family> RequireFamilyAsync(Guid familyId, CancellationToken ct)
        => await db.Families.SingleOrDefaultAsync(f => f.Id == familyId, ct)
            ?? throw new InvalidOperationException("Family not found in the active gym.");

    private async Task RequireAsync(
        FamilyActor actor, Guid familyId, Func<FamilyGuardian, bool> flag, string verb, CancellationToken ct)
    {
        if (actor.IsStaff)
        {
            return; // structure ops are exactly what admins are for (never acting-as)
        }

        if (actor.UserId is not { } userId)
        {
            throw new InvalidOperationException($"You don't have permission to {verb}.");
        }

        var guardian = await db.FamilyGuardians
            .SingleOrDefaultAsync(g => g.FamilyId == familyId && g.GuardianUserId == userId, ct);
        if (guardian is null || !(guardian.IsPrimary || flag(guardian)))
        {
            throw new InvalidOperationException($"You don't have permission to {verb}.");
        }
    }

    private async Task<FamilyGuardian> RequireGuardianAsync(Guid familyId, Guid guardianId, CancellationToken ct)
        => await db.FamilyGuardians.SingleOrDefaultAsync(g => g.FamilyId == familyId && g.Id == guardianId, ct)
            ?? throw new InvalidOperationException("Guardian not found on this family.");

    private static string NormalizeName(string name)
    {
        name = name?.Trim().ToUpperInvariant() ?? "";
        if (name.Length == 0)
        {
            throw new InvalidOperationException("A family name is required.");
        }

        if (name.Length > 80)
        {
            throw new InvalidOperationException("Keep the family name under 80 characters.");
        }

        return name;
    }
}
