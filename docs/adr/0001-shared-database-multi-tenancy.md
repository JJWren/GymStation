# Shared database with TenantId isolation

GymStation is multi-tenant from day one at pilot scale (one gym, ambition of tens to hundreds). We use a single PostgreSQL database where every tenant-owned row carries a `TenantId`, enforced by EF Core global query filters plus a write-side guard in the `SaveChanges` pipeline. Schema-per-tenant and database-per-tenant were rejected: they buy isolation we don't yet need at the price of migration fan-out and per-tenant ops on every release. Postgres row-level security remains available as a later defense-in-depth layer without schema changes.

**Consequences**: every tenant entity must carry `TenantId` and be covered by the query filter; integration tests must assert cross-tenant invisibility; a missed filter is a data-leak bug, not a cosmetic one.
