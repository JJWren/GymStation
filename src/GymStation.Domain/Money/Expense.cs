using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Money;

/// <summary>Owner-defined expense taxonomy — never hardcoded (the Q3/Q4 standing principle).</summary>
public class ExpenseCategory : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }
    public required string Name { get; set; }
    public bool Archived { get; set; }
}

/// <summary>An owner-entered gym outgoing (rent, insurance, equipment).</summary>
public class Expense : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid CategoryId { get; set; }
    public ExpenseCategory Category { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateOnly SpentOn { get; set; }
    public string? Note { get; set; }

    /// <summary>Set when a RecurringExpense materialized this row (idempotency key with SpentOn).</summary>
    public Guid? RecurringExpenseId { get; set; }
}

/// <summary>
/// A monthly standing outgoing (rent, insurance) the worker materializes into a real
/// Expense each month on the chosen day — editable/archivable like everything else.
/// </summary>
public class RecurringExpense : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid CategoryId { get; set; }
    public ExpenseCategory Category { get; set; } = null!;

    public decimal Amount { get; set; }

    /// <summary>1–31; months shorter than the chosen day use their last day.</summary>
    public int DayOfMonth { get; set; }

    public bool Active { get; set; } = true;
    public string? Note { get; set; }

    /// <summary>First day of the last month this recurring materialized an Expense.
    /// The high-water mark IS the idempotency: deleting the materialized expense
    /// doesn't resurrect it next worker pass (#88).</summary>
    public DateOnly? LastMaterializedMonth { get; set; }
}

/// <summary>
/// Money in that isn't dues — seminars, merch, mat fees. Mirrors Expense with a
/// freeform label instead of a category taxonomy; fully editable and deletable,
/// unlike dues payments which are void-only audit records.
/// </summary>
public class OtherIncome : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public required string Label { get; set; }
    public decimal Amount { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public string? Note { get; set; }
}
