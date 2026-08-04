# 4. Templates materialize only from their start date

Date: 2026-08-04

## Status

Accepted

## Context

Template occurrences materialize lazily: viewing a week mints any active
template's occurrence for that week (at most once, via the #168 ClassTemplateWeek
ledger). The mint condition was purely Active + weekday match + not claimed +
not occupied — **nothing bounded when a template is effective**. A template
created today would mint back-dated occurrences into any past week an admin
later browsed (checking attendance history, say), quietly rewriting the
historical calendar.

That wart existed for add-template from v1. Round 4.2 makes template creation
one click (template duplication #179, promote-to-template #180), which
amplifies it.

## Decision

`ClassTemplate.StartDate` (nullable date) bounds materialization: the mint
loop skips any date before it — no session, no ledger claim.

- **Add-template** sets StartDate = the gym's local today. A Monday class
  added on a Thursday starts NEXT Monday; it never retro-fills the current
  week's already-passed days, or any earlier week.
- **Template duplication** sets StartDate = the copy's first minted
  occurrence's date (the chosen weekday in the week being viewed) — the copy
  never appears before the occurrence the admin watched it arrive with.
- **Promote-to-template** sets StartDate = the promoted session's date — the
  series starts exactly where its week-one class sits.
- **Legacy templates stay null = unbounded.** No backfill: guessing historical
  start dates would silently change which past weeks can still materialize,
  and the existing calendar's history is already minted or claimed.

## Consequences

- Browsing past weeks no longer conjures classes that did not exist then —
  for NEW templates. Legacy templates keep the old behavior until edited by
  hand (a deliberate non-goal of this change).
- A paused-then-restored template still mints weeks between pause and restore
  (StartDate only bounds the beginning). If that ever matters, pausing could
  stamp a resume bound — its own decision.
- Editing a template does not touch StartDate; series edits that re-point a
  template's day keep the original bound.
- Tests must construct legacy behavior explicitly (StartDate = null) — the
  service paths always set a bound.
