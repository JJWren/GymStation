# 6. Rank ladders take a per-gym discipline mapping, not a Program FK

Date: 2026-08-07

## Status

Accepted

## Context

People hold ranks in several disciplines (BJJ, Judo, Muay Thai), but nothing
linked a `RankSystem` to a discipline — the "BJJ ladder" and the "BJJ Program"
were related only by naming convention, so no view could label a belt with its
discipline ("Black 3° — but of what?").

The obvious fix — a nullable `GymProgramId` column on `RankSystem` — fails on
tenancy: the IBJJF adult and kids ladders are platform-seeded (`GymId == null`)
and shared by every gym, while `GymProgram` is tenant-owned. A shared ladder
cannot point at one gym's program, and those shared ladders are exactly the
ones most members are on.

Alternatives considered:

1. **FK on custom ladders only** — platform ladders stay unlabeled; the
   most-used ladders never get discipline labels or discipline-scoped
   filtering.
2. **Materialize per-gym copies of the IBJJF ladders** — the FK then works
   everywhere, but at the cost of a heavy migration re-pointing live award
   history and losing the platform/custom distinction.
3. **A per-gym mapping table** — tenancy lives on the link, not the ladder.

## Decision

A tenant-owned link entity, `RankSystemProgramLink` (`GymId`, `RankSystemId`,
`GymProgramId`), unique per (Gym, RankSystem):

- Each gym labels ANY ladder it can see — platform IBJJF or its own custom
  ones — with one of its Programs. Mapping a seeded ladder is allowed because
  nothing on the ladder itself changes; seeded-immutability stands.
- Programs ARE the disciplines. No new Discipline entity: the gym's program
  list (BJJ, Judo, Muay Thai, …) is its discipline list, and a program with no
  ladder (Fitness) simply has no link.
- Display resolves a label per RankSystem: the linked Program's title, falling
  back to the ladder's own name when unmapped. Rank displays (roster, person
  detail, progress, ranks board, promotion history) always carry it.
- Links ride cascade deletes of either side and vanish with their gym's
  tenancy filter like any tenant-owned row.

## Consequences

- Two gyms sharing the IBJJF ladder can label it differently ("BJJ" vs
  "Brazilian Jiu-Jitsu") — the label is marketing vocabulary, which is per-gym
  by nature.
- Discipline-scoped features (roster rank filters, promotion-pipeline reports)
  resolve discipline through the link, so they work for platform ladders too.
- Archiving a program keeps existing links (labels persist) but the picker
  refuses new links to archived programs.
- A gym that never maps its ladders sees exactly today's behavior — ladder
  names as labels.
