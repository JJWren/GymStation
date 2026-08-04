# 5. Family plans price by composition; ward = kid

Date: 2026-08-04

## Status

Accepted

## Context

Family plans were a single flat `Price` charged once to the primary guardian.
Owners asked for a standard rate with an optional increase by family size,
with the standard size configurable — adults and kids separately, and the
freedom to structure a plan as flat, base-plus-extras, or pure per-head, with
family discounts.

Two modeling questions had real alternatives:

1. **What is a "kid" to the biller?** Age (from `Person.DateOfBirth`) or
   wardship (`FamilyMember.IsWard`)? DateOfBirth is nullable, and the glossary
   already rules that "age is a fact; wardship is the modeled state."
2. **How do discounts work?** A percent-off knob implies a reference rate to
   discount from — but members hold different individual plans or none, so no
   unique reference exists.

## Decision

- **Kid = ward.** Pricing counts non-ward FamilyMembers as adults (including
  the primary's own Person once it is a member) and wards as kids. Billing
  follows the modeled state; graduation moves a person to adult pricing at the
  next cycle. Archived Persons are not counted.
- **One formula, four per-plan fields** (`IncludedAdults`, `IncludedKids`,
  `ExtraAdultPrice`, `ExtraKidPrice`):
  `total = Price + max(0, adults − included) × extraAdult + max(0, kids − included) × extraKid`.
  Lanes are strict — unused adult slots never absorb kids. All zeros = the old
  flat rate; zero base with per-head prices = pure per-head.
- **Discounts are baked into the plan's own numbers.** The owner who wants
  "$10 off the $100 rate for family members" types 90. No percent-off field,
  no coupling to individual plans.
- **Comped is the computed total, not the base.** A $0-base per-head plan
  charges sized families; a plan whose computed total is 0 raises nothing.
  The covered set stays price-independent (a comped family still covers).
- **Charges carry the breakdown as columns** (`FamilyAdults`, `FamilyKids`,
  `FamilyExtraAmount`), stamped at raise time. Size is sampled when the cycle
  raises; joins and graduations reprice the NEXT cycle; history is immutable
  and there is no proration.

## Consequences

- "Why is this $180?" is answerable forever from the charge row itself, even
  after the plan's numbers or the family's roster change.
- An 18-year-old still flagged as a ward bills as a kid until graduated —
  intentional; the #92 banner nudges the human decision, never the biller.
- Legacy family charges predate the columns (null breakdown) and stay as-is.
- Existing flat family plans migrate with all-zero sizing — behavior unchanged
  until an owner edits the numbers.
