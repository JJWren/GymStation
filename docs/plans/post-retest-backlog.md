# Post-Retest Backlog — Implementation Plans

Source: Joshua's full v1.0.1/v1.0.2 retests (2026-08-01), triaged into issues #22–#29
(round 1) and #32–#41 (round 2). Each issue below has a plan; the loop works them in
the order listed, one gated PR per issue (branch → implement → verify on the seeded
rig → Copilot review until a clean round → squash merge). Checkboxes track the loop.

All four product decisions were put to Joshua and are **resolved** — see the bottom
section; the affected plans below already reflect the outcomes.

## Execution order

| Wave | Issues | Why this order |
|---|---|---|
| 1 — correctness + quick wins | #32, #38, #35, #33, #36, #37, #40 | Real bugs and small fixes; #32 unblocks #35/#41 |
| 2 — experience decisions | #39, #34, #41 | Need a decision or #32 first; medium size |
| 3 — features | #24, #25, #28, #26, #27, #29, #23, #22 | Big builds; #28 unblocks #25 images; #22 last so the type-scale pass sweeps the final UI |

Releases: one release-please cut per wave (or sooner if a wave runs long) so the lab
gets meaningful drops instead of eighteen tiny ones.

## Loop status

- [x] #32 — Role-aware sign-in routing + guardian gym membership (PR #44)
- [x] #38 — Rank ordering: composite sort, red-belt degrees, NO BELT filter (PR #47 merged)
- [x] #35 — Member schedule: full-day visibility (verify) + guardian states (verified, no code)
- [x] #33 — Real bell icon
- [x] #36 — Gym name → public page; public page knows you (PR #49)
- [x] #37 — Sunday-start calendar stat weeks with dates (PR #50)
- [x] #40 — Admin: edit person details (incl. DOB → IBJJF/Masters category)
- [x] #39 — Instructor experience (/teach landing)
- [x] #34 — Theme toggle
- [x] #41 — Guardian portal
- [x] #24 — Ledger management
- [x] #25 — Events v2 (detail+attendees+past; images follow #28)
- [x] #28 — Shared media upload (preview/crop)
- [x] #26 — Diary v2 (partner rows, entry edit/delete, month calendar)
- [x] #27 — Member check-in history (range chips + custom + paging)
- [x] #29 — Drop-ins / visitors (live-roll quick-add, VISITORS chip, convert)
- [x] #43 — Admin: member portraits from the person page (click-to-upload + crop, staff-only serving)
- [x] #23 — Interactive schedule editor (gym hours, time rail, edit modal; drag deferred)
- [x] #66 — Signed-in identity under the sign-out button (both shells)
- [x] #70 — Pagination for person-heavy lists (roster + dues, shared Pager)
- [x] #22 — Responsive type-scale pass (fluid rem scale, tablet member column, breakpoint screenshots)

---

## Wave 1

### #32 — Role-aware sign-in routing + guardian gym membership

**Bug.** Guardians get zero gyms (`GetGymsForUserAsync` joins Persons only) → no
active-gym claim → Sarah landed on the admin shell's "no active gym" branch.

- `GymMembershipService.GetGymsForUserAsync`: union direct Person gyms with
  `GuardianLinks → child Person → gym` (both `IgnoreQueryFilters`, distinct).
  Same for `IsUserInGymAsync`.
- `AuthEndpoints`: after `SignInWithActiveGymAsync`, redirect by role — Admin/Owner
  → `/`, everyone else → `/schedule`. Same in `/pick-gym`. Extract one
  `DestinationFor(userId, gymId)` helper (queries the Person's roles in that gym;
  guardian-only ⇒ member destination).
- Keep the Home fallback redirect (belt & suspenders for deep links).
- Tests: membership union (guardian sees child's gym), destination matrix
  (owner / member / instructor / guardian-only).
- Verify on rig: sarah.hale login lands on `/schedule` and sees Tom's check-in row.

### #38 — Rank ordering: composite sort, red-belt degrees, NO BELT filter

**Bug.** Sort uses `Rank.Order` alone; Joshua specified the true total order.

- Sort key `(SystemPrecedence, Rank.Order, Stripes)` where none < kids < adult —
  implement as a small pure helper (`RankSort.Key(CurrentRank?)`) in Domain with
  unit tests mirroring Joshua's enumeration verbatim.
- `IbjjfSeed`: append adult ranks Red & Black (7th), Red & White (8th), Red (9th)
  with new stable GUIDs + a migration-safe idempotent seed step (platform ranks are
  HasData/stable-id seeded — follow the existing pattern).
- Roster sort + RanksBoard lane order use the helper; rank filter gains a
  `NO BELT` option (`rank=none` → people with no primary rank).
- Verify: roster `sort=rank` matches the spec order on the seeded cast.

### #35 — Member schedule: full-day visibility (verify-first) + guardian states

- On the 1.0.2 rig, walk yesterday/today/tomorrow and a past week as ana.reyes:
  confirm past sessions and past days render (investigation says they should —
  the original observation predates the date-nav fix).
- Fix any real gap found (e.g., empty-day messaging vs missing materialization).
- After #32: sarah sees Tom's rows on past/future days; empty states read correctly.
- Likely outcome: a verification note + small polish, not a rebuild.

### #33 — Real bell icon

- Inline SVG bell (stroke weight matching the ledger line work) replaces `▲` in
  `MemberLayout`; sweep for other placeholder glyphs used as icons.

### #36 — Gym name links to public page; public page knows you

- Gym name in both shells wraps in `<a href="/{slug}">` (slug already loaded).
- `PublicGym`: read `HttpContext.User` — signed-in visitors see their email, a
  SIGN OUT form, and BACK TO YOUR PORTAL (staff → `/`, else `/schedule`) instead of
  MEMBER SIGN-IN. Anonymous stays exactly as-is (it's the brand face).

### #37 — Sunday-start calendar stat weeks with dates

- `ReportService.WeeklyCheckinsAsync` + `AttendanceService.StatsAsync`: bucket by
  Sunday-start calendar weeks (per Joshua's explicit 8/2, 7/26 spec) instead of
  days-ago÷7; return `(WeekStart, Count)` pairs.
- All three charts print the week date under the count (`M/d`).
- Schedule grids stay Monday-first (D3) — different surface, different job.
- Unit-test the bucketing (edge: today mid-week, exactly-12-weeks-ago).

### #40 — Admin: edit person details

- PersonDetail gains an "Edit person" form (First/Last, DOB, roles checkboxes,
  archive/unarchive toggle) via the (now-correct) SSR form pattern.
- DOB matters beyond names: competition age categories (Master 3, …) derive from it
  (`IbjjfAgeGroup`), so adding/correcting a missing DOB must be first-class — the
  derived IBJJF category on the page updates immediately as proof.
- Guard: cannot remove the gym's last Owner; archiving hides from roster and
  filters (existing `Archived` flag).
- Tests: guard rail; archive round-trip.

## Wave 2

### #39 — Instructor experience (decision 2: instructor landing in member shell)

- New `/teach` landing inside the member shell for Instructor-role sign-ins:
  today's + upcoming sessions they teach (live-roll shortcuts), open cover
  requests actionable inline; member tabs gain TEACH for instructors only.
- Schedule cards distinguish "YOU TEACH" (→ live roll) from attendable (→ check-in).
- Sign-in destination (from #32's helper): Admin/Owner → `/`, Instructor → `/teach`,
  else `/schedule`. Dual Admin+Instructor keeps the admin shell (reaches /teach
  from the rail).

### #34 — Theme toggle (decision 3: per-user DB setting)

- `AppUser.PreferredThemeDark` (nullable bool; null = follow gym default) +
  migration; toggle posts to a small endpoint and re-renders.
- Toggle in admin rail foot + member top bar; applies `.theme-light` at the wrapper
  (mechanism already exists); public page always uses the gym default.

### #41 — Guardian portal (depends #32)

- Guardian sees each linked child: attendance history + rank/progress (read-only
  surfaces mirroring Progress, minus diary).
- Kids' diaries: none in v1 — the diary stays strictly the member's own; a guardian
  proxy would break the ONLY-YOU promise. (Principle call, documented here.)
- Child profile edits stay admin-only for now (#40); guardians get a "tell the front
  desk" hint instead. Revisit if the pilot wants more.
- Dual guardian+member: their own portal is unchanged; child views hang off the
  schedule (existing) plus a "MY PEOPLE" section on Progress listing children with
  links to per-child progress pages.

## Wave 3

### #24 — Ledger management

- Void payments: `VoidedUtc/VoidedByPersonId/VoidReason` columns; voided rows stay
  visible (struck through) and drop out of balance math; ledger math tests updated.
  Never hard-delete money history.
- Plans: rename/price-change (price change affects FUTURE cycles only), archive/
  unarchive; seeded defaults editable like any other.
- Expense categories: manage list (rename/archive) on the dues page.
- Recurring expenses: `RecurringExpense` (category, amount, day-of-month, active);
  `ChargeCycleWorker` (or a sibling worker) materializes them monthly, idempotent
  per (recurring, month) like charges.

### #25 — Events v2

- Event detail page (admin + member): full details, GOING/INTERESTED rosters
  (visible in-gym per the RSVP decision).
- Past browsing beyond 12 months (year pager or "older" cursor).
- Event image (optional) — after #28 lands, reuse the crop component; storage under
  the existing `IFileStore` with a `gyms/{id}/events/{eventId}.{ext}` allowlist route.

### #28 — Shared media upload with preview/crop

- One Blazor component (interactive island — cropping is inherently client-side):
  file pick → canvas preview → drag/zoom crop to the target aspect → posts the
  cropped blob to the existing upload endpoints. Targets: logo (1:1), hero (8:3),
  event (16:9), portraits later.
- Dimension guidance text comes from the component's target profile.
- Fallback: plain upload keeps working (progressive enhancement).

### #26 — Diary v2

- Partner rows: SSR postback pattern — "+ PARTNER" adds a row server-side
  (re-render with n+1), each row has REMOVE; default zero rows. No JS needed.
- Entry detail + edit: click an entry → `/diary/{id}` (own-entries-only by
  construction via TrainingDiaryService); edit notes/rolls/minutes; delete.
- Finding entries: month calendar strip with entry markers + month pager
  (SSR links, no client state).

### #27 — Member check-in history

- Progress gains a CHECK-INS section: list of confirmed/pending records, default
  last 7 days, range chips (7D / 2W / 30D / month picker / YTD / 12MO / custom
  from-to form). Server-side paging past 100 rows.

### #29 — Drop-ins / visitors

- "DROP-IN" quick-add on the live roll (creates an ad-hoc Person flagged
  `Visitor`, no plan, optional contact) + roster VISITORS chip; converts to
  member later by assigning a plan/roles (then the MEMBERS chip means something).

### #23 — Interactive schedule editor (decision 4: modal first)

Foundation:
- `GymSettings.OpenTime/CloseTime` (gym hours) + settings UI.
- Week grid gets a time rail spanning gym hours; session cards position/size by
  time (CSS grid rows per 30min).
- Then per decision: click-to-edit modal first (template + one-off session edits),
  drag-move/resize as the interactive layer (InteractiveServer island for the grid).

### #22 — Responsive type-scale pass (last)

- Replace the `zoom` stopgap with a clamp()-based type scale and spacing audit at
  1920/1440/1366/1024/768/375; fix the iPad-mini awkwardness; screenshot evidence
  per breakpoint in the PR.

---

## ⚖ Decisions — resolved (Joshua, 2026-08-01)

1. **Loop order** — approved as proposed.
2. **Instructor experience (#39)** — instructor-specific landing ("Today: your
   sessions" + live-roll shortcuts) inside the member shell; schedule cards
   distinguish YOU TEACH from attendable. No third shell.
3. **Theme persistence (#34)** — **per-user setting in the DB** (follows the user
   across devices); gym default remains the fallback for users with no preference.
4. **Schedule editor scope (#23)** — time rail + click-to-edit modal first;
   drag-move/resize lands as its own follow-up PR.
