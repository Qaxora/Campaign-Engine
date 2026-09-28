# ADR 0005 — Shared-database multi-tenancy with central enforcement

**Status:** accepted

## Context

Every customer (organization) must only ever see its own campaigns, stores, products, redemptions,
API keys and analytics. Per-tenant databases give strong isolation but complicate migrations,
analytics and operations for a young product. Filtering by hand in every query is the classic source
of data leaks.

## Decision

* One database; every tenant-owned table has a non-null `TenantId`.
* A scoped `ITenantContext` is set by authentication (API key → its organization; session → the
  active membership).
* `CampaignDbContext` applies a **global query filter** `TenantId == current tenant` to every entity
  implementing `ITenantOwned`, and **stamps** `TenantId` on insert. Saving a tenant-owned entity
  without a tenant, or with another tenant's id, throws.
* Natural keys become unique per tenant (`(TenantId, Code)`, `(TenantId, TransactionId)`).
* Caches are keyed by tenant.
* Code that must work across tenants (webhook dispatcher, seeding) opts out explicitly with
  `IgnoreQueryFilters()` and is kept to a minimum.
* Isolation is covered by tests at service and HTTP level.

## Consequences

* Services contain no tenant filters; forgetting one is impossible for normal queries.
* Raw SQL and `IgnoreQueryFilters()` are the only escape hatches and are easy to audit with grep.
* Moving a large tenant to its own database later is possible because every row is labelled.
