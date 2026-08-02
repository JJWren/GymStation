# Round-3 backlog — post-v1.3.0 retest + family/RBAC build-out

Grilled 2026-08-02 (12 decisions; verbatim answers in `aidlc-docs/audit.md`; vocabulary in `CONTEXT.md`).
Loop process per issue: branch off pulled master → implement → rig-verify → PR → Copilot gate until a clean round → resolve threads → squash merge. Tick here in the same PR.

## Waves

| Wave | Issues | Why this order |
|---|---|---|
| A — corrections & polish | #76 #77 #78 #79 #80 #81 #82 #83 #84 #85 #86 | #76 is a live bug; the rest are small, independent, high-visibility |
| B — domain builds | #87 #88 #89 #90 #91 #92 #93 | Staff and Finance first (self-contained); family chain #89→#90→#91→#92 builds on itself; landing editor last |
| C — schedule interactivity | #94 #95 #96 | Events feed the rail before it goes interactive; the island lands last |

## Loop status

- [x] #76 — Active-gym claim lost on security-stamp refresh (fixed: persisted ActiveGymId + claims factory)
- [x] #77 — Un-themed input fields sweep (:not()-based selectors)
- [x] #78 — Admin shell: full-width, sticky rail, hamburger top bar
- [x] #79 — Type scale up, belts 2×, reports density pass
- [x] #80 — Notifications pagination; pager default 10
- [x] #81 — Add person: explicit role/visitor choice required
- [x] #82 — Person contact number + SMS consent
- [x] #83 — Admin↔member switching + explicit landing links
- [x] #84 — Swap-mode/open-claims move to Settings
- [ ] #85 — Footer: privacy · terms · cookies · help
- [ ] #86 — Per-user idle auto-sign-out (off by default)
- [ ] #87 — Staff role + StaffProfile
- [ ] #88 — Finance view (OtherIncome, editable money records, RAISE retired)
- [ ] #89 — Family schema + admin structure management
- [ ] #90 — Family member shell: MY FAMILY + ward switcher + ward diaries
- [ ] #91 — Family plans + primary billing
- [ ] #92 — Ward graduation + 18 nudge
- [ ] #93 — Landing editor (LANDING rail section)
- [ ] #94 — Events structured time + on both schedules
- [ ] #95 — Instructor cover on /schedule
- [ ] #96 — Schedule drag/resize island

## Per-issue notes

### #76 — Active-gym claim (BUG)
`AppUser.ActiveGymId` (migration) + `IUserClaimsPrincipalFactory` re-injecting the active-gym claim on every principal build; `/auth/login` + `/auth/pick-gym` persist the column. Test: regenerated principal keeps tenancy.

### #77 — Field theming
Broaden app.css: `.form-card input:not([type=checkbox]):not([type=radio])` (typeless InputText), `.row-form input[type=number]/[type=search]/[type=file]`, `.filters select`/`.filters input`, range styling. Known offenders: AdminDues (plan name/price, search, new-category, recurring amounts, record-payment), Roster (rank select, first/last), AdminEvents (title, time-info, location), PersonDetail (names, file).

### #78 — Admin shell layout
Drop `.app-frame` max-width; `.rail` → `position:sticky; top:0; height:100vh; overflow-y:auto`; ≤900w: top bar (crest, gym name / GYMSTATION, `<details>` hamburger pull-down with the tab list + foot links).

### #79 — Scale & belts
`html { font-size: clamp(106.25%, 31.25% + 0.9375vw, 140%) }`; `.belt` 1rem / `--s` 0.75rem / `--l` 2rem; AdminReports labels/captions a step up with spacing to match.

### #80 — Pagination
`Pager.DefaultSize = 10`; NotificationsPanel paged via Pager (page/size on `/notifications` + `/admin/inbox`).

### #81 — Add person
VISITOR checkbox; require ≥1 of Member/Instructor/Admin/Visitor; no silent default.

### #82 — Contact
`Person.PhoneNumber` + `Person.SmsAllowed` (migration). Member MY CONTACT card (Progress); admin card (PersonDetail); admin-added default SmsAllowed=false.

### #83 — Cross-shell nav
Admin rail MEMBER VIEW tab (→ /schedule); member top ADMIN link (staff only, → /); VIEW LANDING links in both shells alongside the existing brand-link.

### #84 — Settings consolidation
The two POST toggle forms move from AdminSchedule's left column into AdminSettings; schedule keeps the read-only mode line.

### #85 — Footer
`FooterLinks` shared component on admin rail foot, member shell bottom, public footer. Static pages `/legal/privacy` `/legal/terms` `/legal/cookies` `/help` (platform-authored boilerplate; cookies = essential-only statement; help = member + admin basics). Joshua reviews copy in the PR.

### #86 — Idle sign-out
`AppUser.IdleSignOutMinutes` (null = off) + admin-settings control (current user); `idle-signout.js` timer reads a shell data-attribute, POSTs `/auth/logout`, lands `/login`.

### #87 — Staff
`PersonRoles.Staff`; migrate InstructorProfile → StaffProfile (table rename, same fields); PersonDetail STAFF PROFILE section for Instructor|Admin|Owner|Staff; Roster STAFF chip; public instructor listing unchanged.

### #88 — Finance
Route `/admin/finance` (redirect from /admin/dues); nav FINANCE. `OtherIncome` entity (label/category, amount, receivedOn, note) + Ledger add/update/delete for income AND expenses; `RecurringExpense.LastMaterializedMonth` high-water mark (delete ≠ resurrect); remove RAISE button + `/run-cycle`; MonthSummary/Reports include other income (net = dues + income − expenses).

### #89–#92 — Family chain
89: entities + GuardianLink migration (each existing link → single-guardian family, guardian primary iff they have a Person... primary requires a Person — links whose guardian lacks a Person migrate with admin-fixup flag) + `FamilyService` (authz matrix central) + admin Families surface + PersonDetail card.
90: MY FAMILY page; ward switcher (acting context across schedule/check-in, RSVP, progress, events, ward diaries via TrainingDiaryService ward paths gated on ActForWards+IsWard).
91: `MembershipPlan.Scope`; `Family.MembershipPlanId`; cycle charges primary's Person, skips covered members; ViewBilling gate.
92: graduation action (primary/admin): invite login, clear IsWard, diary fully privatized; 18th-birthday nudges (primary + admins).

### #93 — Landing editor
GymSettings landing fields (tagline anchor labels; visit address/phone/email/socials); `Admin/Landing.razor` rail item; hero/logo widgets move here; PublicGym renders real anchors + VISIT section.

### #94 — Events time
`GymEvent.StartTime?`/`DurationMinutes?` (migration); publish/edit forms; timed cards on admin rail + member day list (star/dashed styling, link to event); untimed = all-day banner.

### #95 — Cover on /schedule
MemberSchedule loads open substitution requests for instructor viewers: NEEDS COVER stamp + claim/accept form (`back=schedule` allow-list); Teach retargets ALL SUBSTITUTIONS → /schedule; `/instructor/swaps` policy → GymStaff.

### #96 — Drag island
Rail grid extracted to a component with `@rendermode InteractiveServer` (admin schedule only); pointer drag-move + bottom-edge resize, 30-min snap (15-min floor), occurrence-only via existing `UpdateSessionAsync`; static modal flow stays the fallback.

## Verification standard
Suite green + new tests per issue (claims persistence; family authz matrix on the father/mother/grandparent/2-wards fiction; primary billing skip; graduation privatization; staff profile; income/expense edit/delete; high-water materializer). Rig with the family-of-5 added to DemoSeeder. Playwright breakpoint screenshots for #78/#79 (1920/1366/900/375). Lab deploys grep migration names before psql.
