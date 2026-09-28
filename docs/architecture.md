# Architecture

## 1. Problem

A typical mid-size retailer runs several systems that all "do promotions":

| System | Typical product | How campaigns are built |
|---|---|---|
| ERP | SAP (promotion modules unused) | — |
| Legacy ERP / back office | in-house, 15+ years old | forms + hand-written SQL |
| Store POS | in-house or vendor software (often Delphi) | synced from the legacy ERP |
| E-commerce | Akinon, Ticimax, … | the platform's own generic promotion module |

The result:

* **Campaigns collide across channels.** A store campaign and an e-commerce campaign cover the same
  products in the same week and stack into a discount nobody approved.
* **Product scopes are maintained with SQL.** Adding products, or excluding products that must never
  be discounted (tobacco, gold, gift cards, products under legal price regulation), is done by
  running ad-hoc `INSERT`/`DELETE` statements.
* **Every channel computes discounts differently.** The same basket gets a different total in the
  store and online, so returns and customer complaints become expensive.

## 2. Goals

1. **One engine, one rule model, every channel.** Campaigns are defined once and evaluated the same
   way for POS, web, mobile, call center or marketplace orders.
2. **Integrates with anything.** Plain JSON over HTTP, no client library required. First-class
   samples for .NET, Python and Delphi.
3. **Explainable.** Every result says which campaign gave how much discount to which line — and,
   on request, why every other campaign did *not* apply.
4. **Safe by default.** Global exclusion lists, price floors, per-order caps, budgets and usage limits
   are enforced by the engine, not by convention.
5. **Conflict detection before go-live.** Overlapping campaigns are reported when a campaign is
   activated, not when a customer complains.
6. **Easy to extend.** New condition/reward types are one class each.

Non-goals (for v0.1): base price management, loyalty points ledger, UI. The engine is headless.

## 3. Big picture

```
                 ┌────────────────────────────── Campaign Engine ───────────────────────────────┐
  Back office    │  REST API (ASP.NET Core)                                                     │
  (admin UI,     │   /api/campaigns      manage + lifecycle + conflict analysis                 │
   scripts) ───► │   /api/product-lists  include / exclude lists, CSV import                    │
                 │   /api/webhooks       change notifications (HMAC signed)                     │
                 │                                                                              │
  POS (Delphi) ─►│   /api/evaluate       cart in → discounts out   (stateless)                  │
  E-commerce   ─►│   /api/redemptions    confirm / reverse / import offline sales (stateful)    │
  Mobile (Py…) ─►│   /api/snapshot       all live rules for local/offline evaluation (ETag)     │
                 │                                                                              │
                 │  CampaignEngine.Core  (pure, no I/O)  ◄── also usable in-process by .NET apps │
                 │  CampaignEngine.Infrastructure  EF Core (SQLite / SQL Server / PostgreSQL)   │
                 └──────────────────────────────────────────────────────────────────────────────┘
```

Two integration modes, borrowed from SAP Omnichannel Promotion Pricing:

* **Central** — the channel calls `POST /api/evaluate` for every basket change. Simplest, always
  consistent. Recommended default.
* **Local** — the channel downloads `GET /api/snapshot` (polling with `If-None-Match`, or triggered
  by a webhook) and evaluates in-process. .NET channels reference `CampaignEngine.Core` directly.
  Useful for stores with unreliable connectivity; offline sales are reported later through
  `POST /api/redemptions/offline`.

## 4. Projects

| Project | Depends on | Responsibility |
|---|---|---|
| `CampaignEngine.Core` | BCL only | Domain model, rule types, evaluation engine, conflict analyzer, validation, JSON contract |
| `CampaignEngine.Infrastructure` | Core, EF Core | Persistence, redemption ledger, usage counters, outbox, catalog cache |
| `CampaignEngine.Api` | Core, Infrastructure | HTTP endpoints, API-key auth, OpenAPI, webhook dispatcher |
| `CampaignEngine.Client` | Core | Typed .NET HTTP client |

`Core` has no dependency on ASP.NET or EF Core so it can be embedded in a POS, a background job or a
test without pulling in a web stack.

## 5. Rule model

A campaign is a JSON document:

```jsonc
{
  "code": "SUMMER-3X2",
  "name": "3 for 2 on t-shirts",
  "status": "active",
  "priority": 100,                      // higher is applied first
  "stacking": "stackable",              // stackable | exclusive
  "exclusivityGroup": null,             // at most one campaign per group applies (the best one)
  "schedule": { "startsAt": "...", "endsAt": "...", "timeZone": "Europe/Istanbul",
                "daysOfWeek": ["saturday","sunday"], "dailyStart": "10:00", "dailyEnd": "14:00" },
  "channels": ["store", "web"],         // empty = every channel
  "stores": { "include": [], "exclude": ["IST-001"] },
  "customerSegments": [],               // empty = everyone
  "coupon": { "codes": ["SUMMER25"], "maxUsesPerCode": null },   // null = no coupon needed
  "target": { "categories": ["tshirt"], "excludeProductLists": ["NO-DISCOUNT"] },
  "conditions": [ { "type": "minQuantity", "quantity": 3 } ],
  "reward": { "type": "buyXGetY", "buyQuantity": 2, "getQuantity": 1 },
  "limits": { "maxRedemptions": 10000, "maxRedemptionsPerCustomer": 1,
              "budget": 250000, "maxDiscountPerOrder": 500 }
}
```

* **Target** (`ProductSelector`) — which cart lines the reward applies to. Includes SKUs, categories,
  brands, attributes and named product lists; every criterion has an `exclude*` counterpart.
* **Conditions** — all must hold. Composites (`allOf`, `anyOf`, `not`) allow arbitrary logic.
* **Reward** — exactly one per campaign. Complex offers are modeled as several campaigns sharing an
  exclusivity group.

See [rule-reference.md](rule-reference.md) for every type.

## 6. Evaluation algorithm

```
candidates = campaigns where
      status = active
  and schedule matches cart.timestamp (in the campaign time zone)
  and channel / store / segment match
  and coupon (if required) is present
  and usage limits and budget are not exhausted
  and all conditions hold on the cart

plans = [ stackable plan ] + [ one plan per exclusive candidate ]
stackable plan = stackable candidates, one winner per exclusivity group
                 (the member giving the largest discount on its own), sorted by priority desc

for each plan: apply rewards in order on a working copy of the cart
pick plan by strategy: BestForCustomer (default) | HighestPriority
```

While applying rewards the engine keeps, per cart line, the **net amount so far** and the **units
locked** by unit-based rewards (buy-X-get-Y, bundles, fixed unit price). Consequences:

* Percentage and amount discounts stack multiplicatively on what is left, never on the list price
  twice.
* The same physical unit is never used by two unit-based offers ("3 for 2" + "2 for 100").
* A unit never goes below its **price floor** (`cartLine.minimumUnitPrice`) — the retail
  "minimum viable price".
* Lines with `discountable: false` and SKUs in a **global exclusion list** are never touched.

Every discount is allocated to lines with a largest-remainder algorithm so that line allocations add
up to the campaign total to the cent. Line allocations are what a POS prints on the receipt and what
returns are refunded against.

## 7. Redemptions and limits

`/evaluate` is stateless and can be called any number of times. When a sale is completed the channel
calls `POST /api/redemptions` with its **transaction id** and the final cart. The engine re-evaluates
(so a client cannot claim discounts it is not entitled to), checks limits inside a serializable
transaction, and stores one ledger row per applied campaign.

* The call is **idempotent** on the transaction id: retries return the stored result.
* `POST /api/redemptions/{transactionId}/reverse` releases usage and budget on cancellation.
* `POST /api/redemptions/offline` imports sales that were evaluated locally while offline.
  They are recorded as-is and flagged, because the sale already happened.

## 8. Conflict analysis

`POST /api/campaigns/conflicts` (and implicitly `activate`) compares a campaign with every other
live campaign. Two campaigns *overlap* when their schedules, channels, stores and product scopes
can intersect. Overlaps are classified:

| Kind | Severity | Meaning |
|---|---|---|
| `duplicateCoupon` | error | same coupon code in two overlapping campaigns |
| `stackedDiscount` | warning | both stackable on the same products; combined discount estimate given |
| `exclusiveOverlap` | info | an exclusive campaign competes with the other one |
| `samePriority` | warning | overlapping non-stackable campaigns with equal priority — order is ambiguous |
| `sameGroup` | info | same exclusivity group; only the better one will apply |

Activation is refused on errors unless `force=true`.

## 9. Distribution: webhooks and snapshot

Every change to a campaign or product list writes an **outbox** row in the same database
transaction. A background dispatcher delivers it to subscribed URLs with retries and an HMAC-SHA256
signature (`X-Campaign-Signature: sha256=<hex>`). Receivers typically react by refreshing their
snapshot.

`GET /api/snapshot` returns all live campaigns and the product lists they reference, with a strong
`ETag`. Clients poll with `If-None-Match` and get `304 Not Modified` when nothing changed.

## 10. Security

API keys (`X-Api-Key` header) with two roles: `admin` (manage campaigns) and `channel`
(evaluate/redeem/snapshot). Keys are configured per client system so the ledger can record who
redeemed what. Put the service behind TLS termination.

## 11. Extending

* **New condition** — implement `Condition`, register the JSON discriminator in `Condition.cs`.
* **New reward** — derive from `Reward`, implement `Apply(RewardContext)`, register the discriminator.
* **New database** — add an EF Core provider in `Infrastructure/DependencyInjection.cs`.

Architecture decisions are recorded in [docs/adr](adr).
