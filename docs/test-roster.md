# GymStation — Standard Test Tenant (round 4.5)

The standard test tenant is **Testworks Combat Club** (`/testworks` on the test
stack): 300 people across four disciplines, every account archetype
sign-in-able, and every pathology the warning surfaces exist for — seeded on
purpose. Seed with `POST /ops/seed-standard` (`X-Ops-Key` + a ≥10-char
`seedPassword`); **every** seeded `@testworks.demo` login gets that password.
The seed is deterministic: its counts are pinned by `StandardSeederTests` — if
you change the seeder, you are changing the spec.

## Named logins (all share the seed password)

| Login (`…@testworks.demo`) | Who | What it tests |
|---|---|---|
| `val.moreau` | Owner + Admin | The whole admin story |
| `quinn.barlow` | Admin only (no membership) | Admin-without-Member flows |
| `ren.ito` | Admin + Member (Adult plan) | Staff who also train |
| `mateus.rocha` | BJJ head coach (Instructor + Member, comped) | Dual-role, /teach, swaps |
| `talia.nunes` | BJJ second coach | Cover claims, kids BJJ |
| `anong.sit` | Muay Thai coach (Instructor ONLY) | Instructor-without-Member shape |
| `hana.yoshida` | Judo coach | Second custom ladder |
| `dee.cross` | Fitness coach | Unranked-discipline coach |
| `iris.vale` | Member, **1 month behind** | Dues notice → /dues |
| `cole.draper` | Member, **3 months behind** | Deep arrears aging |
| `nils.berg` | Member pointing at an ARCHIVED plan | DORMANT plan rendering |
| `noa.feld` | Adult in Feld family, personal plan DORMANT | #197 covered caption |
| `gus.feld` | **Training parent** — primary guardian linked to a member Person | #191 shape, family billing lands here |
| `ada.okonkwo` | Primary of the OVER-size family (2A+4K on 2+2) | #181 extras billing ($190) |
| `reka.varga` | Primary of the per-head family ($0 base) | Zero-base computed billing ($180) |
| `bram.ashford` | Family primary whose Person is NOT a member | Bills-both trap + row warning |
| `dana.morrow` | Guardian login, NO roster Person (ward Finn on Kids BJJ plan) | Leo shape: ward-dues notices, chips, /dues/child |
| `remy.baptiste` | Same shape, Kids Judo ward (Zoe) | Second Leo shape |
| `mora.holt` | Guardian, no Person; ward Theo (16) HAS a login | Sarah shape + teen ward |
| `theo.holt` | Teen ward with own login | Ward portal experience |
| `emi.nakamura` | Guardian of an 18+ ward (Kai) | Graduation-nudge banner |

No-login persons worth knowing: **Pat Winters** (Staff-only desk, #87-clean),
**Sky Tanaka / Jo Marsh** (visitors — convert one via SET PLAN), **Ruth Calder /
Sol Ambrose** (archived, history kept), **Aldo Pinto / Vera Lobo / Iwao Sato**
(the red belts), **Finn Morrow / Zoe Baptiste** (billed wards), and the family
kids.

## Composition (pinned by tests)

- **300 people**: 228 adults / 72 kids-program members.
- Disciplines: BJJ-only ~108, MT-only 30, Judo-only ~17, Fitness-only ~23, mixed ~46; kids: 48 BJJ, 18 Judo, 6 both.
- **Plans**: Adult Unlimited $85 · Muay Thai $70 · Judo $60 · Fitness $50 · Kids BJJ $65 · Kids Judo $60 · Family Standard $150 (2+2 included, +$30/adult +$20/kid) · Family Per-Head ($0 + $80/$50) · Comped $0 · Legacy Unlimited $75 (ARCHIVED).
- **Arrears: exactly 14 behind** — 7×1 month, 5×2 months, 2×3 months. Everyone else current across 3 cycles.
- **Ranks**: every rank of all four ladders is held — IBJJF adult (incl. the three red belts) and kids (all 13), Muay Thai Prajioud (6), Judo (7).
- **Schedule**: ~26 weekly templates (StartDate 12 weeks back), 12 weeks of attendance WITH mint-ledger claims, one paused template, one cancelled session, one open substitution request on next Monday's Adv Gi, one one-off (promote candidate).
- **Comms**: a timed seminar + a tournament with RSVPs, unread notifications for owner/coach/members/guardian, 2 unread contact messages.

## Reset runbook (test stack)

The test stack lives at `Z:\docker\gymstation-test` (postgres container
`gymstation-test-db`, web on **:8630**, `Database__MigrateOnStart=true`). It is
fully disposable — the reset script is the only supported way to change its
data:

1. `scripts/reset-test-stack.ps1` — pulls the latest release image, `compose
   down -v` (drops the volume), `up -d` (the app migrates itself on boot),
   waits healthy, then seeds via `/ops/seed-standard` and probes `/testworks`.
   Secrets (`OPS_API_KEY`, `POSTGRES_PASSWORD`, `SEED_PASSWORD`) come from the
   stack's `.env`. Logs land in `Z:\docker\gymstation-test\logs\`.
2. `scripts/register-test-reset-task.ps1` — one-time: registers the Windows
   scheduled task (nightly 04:00 by default; `-Cadence Weekly` for weekly).
3. The script REFUSES to run against any compose file whose postgres isn't
   named `gymstation-test-db` — the live lab (`gymstation-db`) is out of reach
   by construction.
4. The stack rides the EXTERNAL Docker network `gymstation-test-net` (the
   reset script creates it if missing) so companion containers survive resets.
   **pgAdmin** lives at `Z:\docker\pgadmin` on that network (plus the lab's),
   browsable at `http://localhost:5050` with zero database ports exposed —
   both servers come pre-registered; database passwords live in each stack's
   `.env`. If you ever fully `compose down` the LAB stack, stop pgAdmin first
   (it holds an endpoint on the lab's network).
