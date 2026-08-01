# GymStation — Owner Pitch Walkthrough

The demo tenant is **Ironworks BJJ** (`/ironworks-bjj`): ~50 people, 12 weeks of confirmed
attendance, a live ledger with six members behind on dues, last month's expense log, and
three published events. Seed it with `POST /ops/seed-demo` (see compose.yaml header);
the password you pass becomes the login for every account below (`@ironworks-bjj.demo`):

| Login | Who | Shows |
|---|---|---|
| `jordan.torres@…` | Owner/Admin | The whole admin story |
| `rui.silva@…` | Head coach (Instructor + Member — one record) | Dual-role, swaps, live roll |
| `ana.duarte@…` | No-gi coach | Claiming open cover requests |
| `ana.reyes@…` | Purple-belt member | The member portal |
| `sarah.hale@…` | Guardian (no roster record of her own) | Guardian check-in for Tom |

## The 10-minute script

1. **Public page** — open `/ironworks-bjj` signed out: their brand color, live week strip,
   instructor bios. *"This is what someone googling you sees. It's already themed to you."*
2. **Today** — sign in as Torres. The attention queue: who's behind on dues, what needs
   cover, today's roll counts. *"Everything that needs you, one screen."*
3. **Roster** — search, filter, belt bars everywhere. Open **Ana Reyes**: time-at-belt
   derived from her promotion history, real attendance bars, ledger with a derived
   balance. Record a stripe live — takes five seconds.
4. **Ranks board** — the whole academy by belt. *"Promotion day planning without the
   spreadsheet."*
5. **Schedule** — cancel a class with a reason (members are notified), then the
   substitution panel: this gym runs admin-gate, so show the approve step. Flip the gym
   setting to auto-apply to show it's their choice.
6. **Dues** — the behind-list is oldest-first with one-tap payment recording.
   *"You keep collecting money however you already do — this just always knows who's paid."*
7. **Reports** — money in vs out from real ledger data, then the **break-even calculator**
   prefilled from their live numbers. Change staffing % in front of them. *"Every input is
   yours — nothing about your cost structure is assumed."*
8. **Member portal** — switch to Ana Reyes on a phone-sized window: check in to tonight's
   class, log a diary entry (point out the ONLY-YOU banner), show two-tier mat hours.
   *"Your students get something out of this too — that's why they'll actually check in."*
9. **Guardian** — as Sarah Hale, check Tom in. *"Kids without phones are a first-class case."*
10. **Settings** — change the accent color live (show the contrast guard), upload their
    real logo if they have one on hand. *"Five minutes to make it theirs."*

## Standing answers

- **"What about payments?"** — Deliberately not in v1: we track, you collect however you
  already do. Stripe automation is on the board once you tell us what you'd want.
- **"Who can see a student's diary?"** — Nobody but the student. Not instructors, not you.
- **"What if a coach forgets to confirm the roll?"** — It confirms itself two hours after
  class ends; coaches only fix mistakes.
