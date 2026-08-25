# GymStation

Multi-tenant management platform for combat-sports gyms — BJJ first. GymStation cuts gym-owner admin time (paid-member tracking, promotions, class schedules) and gives members a training portal (check-ins, mat hours, promotion history, private training diary).

**Status:** v1 in development. Built pilot-gym-first: one real academy's workflows drive scope; multi-tenant from day one.

## Try the demo

A fully seeded demo gym is live at **<https://test.gymstation.app>** — sign in as
`val.moreau@testworks.demo` with password `Testworks!Seed2026` for the owner's view,
or pick from [two dozen demo logins](docs/DEMO.md) showcasing coaches, members in
arrears, families, and guardians. The demo database resets nightly at 4 AM US Central,
and it's a shared public sandbox — don't enter real personal data.

## Stack

- .NET 10 unified Blazor Web App — SSR public gym pages (`/{gym-slug}`), interactive authenticated app, API endpoints, ASP.NET Identity in one host
- PostgreSQL + EF Core — shared database, `TenantId` isolation via global query filters
- Docker Compose deploy; GitHub Actions CI; release-please + GHCR images

## Solution layout

```
src/GymStation.Domain          entities and invariants (no EF)
src/GymStation.Infrastructure  EF Core, migrations, storage, email
src/GymStation.Web             Blazor Web App host
tests/GymStation.Domain.Tests
tests/GymStation.Integration.Tests
design/                        Academy Ledger design system (synced to Claude Design)
```

## Development

```bash
dotnet build GymStation.slnx
dotnet test GymStation.slnx
```

## Docs

- [CONTEXT.md](CONTEXT.md) — the ubiquitous language (read this first)
- [docs/DEMO.md](docs/DEMO.md) — the public demo site and its cast of logins
- [docs/adr/](docs/adr/) — architecture decision records
- [aidlc-docs/audit.md](aidlc-docs/audit.md) — requirements/design session audit trail
