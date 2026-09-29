# Qaxora Campaign — SaaS architecture

Qaxora Campaign turns the Campaign Engine into a multi-tenant **retail promotion intelligence
platform** served at `campaign.qaxora.com`. This document describes how the SaaS layers sit on top of
the existing engine. The engine itself is described in [architecture.md](architecture.md).

## Principles

1. **The engine is the source of truth.** Pricing, stacking, limits and conflicts are computed by the
   deterministic `CampaignEngine.Core`. Neither the web app nor the AI assistant re-implements a rule.
2. **Modular monolith.** One deployable API, clear module boundaries, one database. Modules can be
   split out later without changing their contracts ([ADR 0004](adr/0004-modular-monolith.md)).
3. **Tenant isolation is structural.** Every tenant-owned row carries `TenantId`; filtering and stamping
   happen in one place ([ADR 0005](adr/0005-multi-tenancy.md)).
4. **AI proposes, humans decide.** The assistant turns language into *proposals* that go through the
   same validation and conflict analysis as hand-made campaigns; it never activates, prices or
   authorizes anything ([ADR 0006](adr/0006-ai-assistant-boundaries.md)).
5. **Thin web app.** Next.js renders and orchestrates; it holds no business rules and no tokens in the
   browser ([ADR 0007](adr/0007-web-bff-and-sessions.md)).

## Module map

```
                    campaign.qaxora.com (Next.js, BFF)
                                │  session cookie → bearer token (server side only)
                                ▼
 POS / e-commerce ──API key──►  CampaignEngine.Api  (one ASP.NET Core process)
                                │
   ┌────────────────────────────┼──────────────────────────────────────────────┐
   │ Organization   tenants, users, memberships, sessions, API keys, stores     │
   │ Catalog        products, product lists, global exclusions                  │
   │ Campaign       definitions, lifecycle, validation         ─┐               │
   │ Conflict       ConflictAnalyzer (Core)                     ├─ Core engine  │
   │ Evaluation     PromotionEvaluator (Core)                  ─┘ (pure, no I/O)│
   │ Redemption     ledger, usage counters, idempotency, reversal               │
   │ Analytics      read models computed from campaigns + ledger                │
   │ Integration    webhooks (outbox), snapshot, integration API                │
   │ Audit          who did what                                                │
   │ AI             IAiCampaignAssistant → Ollama (replaceable)                 │
   └──────────────────────────────┬─────────────────────────────────────────────┘
                                  ▼
                         PostgreSQL (SQLite for local development)
```

| Module | Project / folder |
|---|---|
| Engine, conflicts, JSON contract | `src/CampaignEngine.Core` |
| Organization, catalog, campaigns, redemption, analytics, integration, audit | `src/CampaignEngine.Infrastructure/<Module>` |
| AI | `src/CampaignEngine.Ai` (no dependency on persistence; talks to the engine through contracts) |
| HTTP surface | `src/CampaignEngine.Api/Endpoints` |
| Web | `web/` |

## Identities

| Who | Authenticates with | Tenant comes from | Typical permissions |
|---|---|---|---|
| Person using the web app | Session token (from login) | Active membership of the session | Owner / Admin / Member |
| POS, e-commerce, ERP | API key (`X-Api-Key`) | The key's organization | `channel` and/or `admin` scope |

Authorization is expressed as policies (`read`, `manage`, `channel`, `owner`) that both identity kinds
map onto, so endpoints do not care who is calling.

## Request flow

```
request → authentication (API key | session) → tenant context set
        → authorization policy → endpoint → module service
        → EF Core (global tenant filter, TenantId stamped on insert) → response
```

Background work (webhook delivery) runs without a request. It is the only code allowed to bypass the
tenant filter, and it does so explicitly.

## AI flow

```
natural language ─► AI provider (Ollama llama3.1:8b by default)
                 ─► intent JSON (schema-constrained)
                 ─► mapped to a CampaignDefinition in C# (catalog-aware)
                 ─► CampaignValidator + ConflictAnalyzer   (deterministic)
                 ─► proposal shown to a human
                 ─► human saves it as a DRAFT ─► human activates it (normal activation rules)
```

## Plans and billing

Organizations carry a plan (`free`, `growth`, `enterprise`). v1 does not charge or enforce limits;
the plan exists so that limits and a billing provider can be added without changing the tenant model
(issue #28).

## Deployment shape

* `campaign.qaxora.com` → Next.js (Node) → `CAMPAIGN_API_URL` (internal) → API.
* Integration clients call the API directly on a public hostname (e.g. `api.campaign.qaxora.com`).
* PostgreSQL; one database, all tenants.
* Ollama (or another provider) reachable from the API only.

## Audit log

Every configuration change is recorded in `audit_entries` in the same database transaction as the
change itself: campaign lifecycle and product lists (through the `IChangeNotifier` that also feeds the
webhook outbox), stores, API keys (never their secrets), webhooks, members and organization settings.
Each entry names the actor (user email or API key name), the action (`campaign.activated`), the entity
and a one-line summary. `GET /api/v1/audit` lists them per organization with entity / action / actor
filters; redemptions are not audited here because the ledger already records them.
