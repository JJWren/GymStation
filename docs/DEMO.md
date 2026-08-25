# GymStation Demo — Testworks Combat Club

**Demo site:** <https://test.gymstation.app> · **Public gym page:** <https://test.gymstation.app/testworks> · **Sign in:** <https://test.gymstation.app/login>

The demo runs a fully seeded fictional gym — **Testworks Combat Club** — with about
300 people, ~26 weekly classes across BJJ, Muay Thai, Judo, and Fitness, four belt/rank
ladders, family plans, guardians, overdue dues, events with RSVPs, and 12 weeks of
attendance history. Everything is fake, and everything is yours to poke at.

> [!IMPORTANT]
> **The demo resets every night at 4:00 AM US Central.** The database is wiped and
> reseeded from scratch, so anything you create is destroyed daily and the site may be
> briefly unavailable for a minute or so around then.
>
> **This is a shared, public sandbox.** Other visitors use the same accounts and can see
> anything you enter — please don't type in real names, emails, or any personal data.

## Signing in

Every demo account uses the **same password**:

```
Testworks!Seed2026
```

Emails all end in `@testworks.demo` — a fake, non-routable domain; no real mailboxes
exist. There is no self-serve signup: the accounts below are the way in.

Where you land after sign-in depends on the account's role: admins land in the back
office, instructors land on the teaching view (`/teach`), members land on the class
schedule (`/schedule`). Accounts with more than one hat get asked which view they want.

## The cast

Each account below was seeded to showcase a specific scenario — the third column tells
you what's interesting about it.

### Staff & admin

| Login | Who | What to look at |
|---|---|---|
| `val.moreau@testworks.demo` | Val Moreau | **The owner.** Sees everything: dashboard, finances, reports, roster, ranks, schedule, events, messaging, settings. Start here. |
| `quinn.barlow@testworks.demo` | Quinn Barlow | Admin with every capability granted — but not a member, so no training life of their own. |
| `ren.ito@testworks.demo` | Ren Ito | Admin **and** member, with only a partial set of admin capabilities — see what a limited back-office grant looks like. |
| `mateus.rocha@testworks.demo` | Mateus Rocha | BJJ head coach (instructor + member on a comped plan). Class rosters, attendance marking, substitutions. |
| `talia.nunes@testworks.demo` | Talia Nunes | Second BJJ coach, instructor + member. |
| `anong.sit@testworks.demo` | Anong Sit | Muay Thai coach who **only** teaches — instructor without a membership. |
| `hana.yoshida@testworks.demo` | Hana Yoshida | Judo coach, instructor + member. |
| `dee.cross@testworks.demo` | Dee Cross | Fitness coach, instructor + member. |

### Members with a story

| Login | Who | What to look at |
|---|---|---|
| `iris.vale@testworks.demo` | Iris Vale | One month behind on dues — the gentle end of arrears. |
| `cole.draper@testworks.demo` | Cole Draper | Three months behind — the deep end. |
| `nils.berg@testworks.demo` | Nils Berg | Still pointed at an **archived** legacy plan; shows the dormant-plan state. |
| `noa.feld@testworks.demo` | Noa Feld | Personal plan dormant because a family plan covers them. |
| `theo.holt@testworks.demo` | Theo Holt | 16-year-old with his own login (his guardian has one too — see below). |
| `kai.nakamura@testworks.demo` | Kai Nakamura | Just turned 18 — subject of the kids-to-adult graduation nudge. |

### Families & guardians

| Login | Who | What to look at |
|---|---|---|
| `gus.feld@testworks.demo` | Gus Feld | Training parent — family primary who also trains himself. |
| `ada.okonkwo@testworks.demo` | Ada Okonkwo | Primary of an over-size family (extra heads beyond the plan's included seats). |
| `chidi.okonkwo@testworks.demo` | Chidi Okonkwo | The second adult in that family. |
| `reka.varga@testworks.demo` | Reka Varga | Primary on the per-head family plan (pay per person, no base bundle). |
| `bram.ashford@testworks.demo` | Bram Ashford | Pays for a family **without being in it** — the payer-outside-the-family shape. |
| `dana.morrow@testworks.demo` | — | **Guardian-only login** (no member profile of their own) managing a kid on Kids BJJ. |
| `remy.baptiste@testworks.demo` | — | Guardian-only login, kid on Kids Judo. |
| `mora.holt@testworks.demo` | — | Guardian of 16-year-old Theo, who also has his own login. |
| `emi.nakamura@testworks.demo` | — | Guardian whose ward (Kai) just turned 18. |

Beyond the named cast, roughly 100 everyday member logins exist with the same password —
the seed is deterministic, so the same people (like `arden.birch@testworks.demo`) come
back after every reset.

## Suggested tour

1. Sign in as **`val.moreau`** and walk the back office: dashboard, member roster,
   dues (14 people are in arrears on purpose), rank ladders, reports.
2. Switch to **`mateus.rocha`**, open `/teach`, pick tonight's BJJ class, and mark
   attendance.
3. Sign in as **`iris.vale`** or **`gus.feld`** to see the member portal — schedule,
   own dues, ranks, and training history.
4. Try a guardian (**`dana.morrow`**) to see the manage-your-kids view.

## For developers

- **The reset** is a scheduled job on the host: every night at 04:00 America/Chicago it
  pulls the latest `master` image, tears the stack down **including volumes** (database
  and uploaded media are destroyed), brings it back up (the app migrates its own schema
  on start), and reseeds via the key-protected `POST /ops/seed-standard` endpoint. The
  whole cycle takes under a minute. The ops endpoints return 404 unless an ops key is
  configured, so an unconfigured deployment exposes nothing.
- **Seeding is deterministic** — a fixed RNG seed means the same 300 people, plans,
  schedules, and edge cases every night, and the integration tests pin those counts.
  The full roster narrative lives in [`docs/test-roster.md`](./test-roster.md).
- **Accounts only exist via seeding.** Seeded users are created with no password;
  the seed endpoint activates the `@testworks.demo` logins with the shared demo
  password. There is no registration flow.
- The shared single password is a demo convenience on a disposable database — it is
  not how the platform handles real credentials, and the demo isn't a statement about
  password policy.
- Want your own instance? See the [README](../README.md) for the stack and local
  development setup.
