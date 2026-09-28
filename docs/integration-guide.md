# Integration guide

How to connect the systems of a typical retailer to Campaign Engine.

## 1. Decide who owns campaigns

Campaign Engine works best as the **single source of truth**. Campaigns that today live in the
legacy ERP, in the POS and in the e-commerce platform's promotion module are recreated here, and the
channels stop computing discounts themselves. During migration a channel can keep its own
promotions for a while; run the conflict analysis (`GET /api/v1/campaigns/conflicts`) regularly to see
what overlaps.

## 2. Create API keys per system

Keys belong to your organization and are created in the web app (Integrations → API keys) or through
the API with an admin key:

```http
POST /api/v1/api-keys
{ "name": "pos-istanbul", "scopes": ["channel"] }

→ 201 { "id": "…", "name": "pos-istanbul", "prefix": "qxc_Ab3dE7Hk", "scopes": ["channel"], "key": "qxc_…" }
```

The `key` is shown **only once**; the platform stores a SHA-256 hash. Use one key per system so the
`name` recorded on every redemption tells you which system sold what, and so a leaked key can be
revoked (`POST /api/v1/api-keys/{id}/revoke`) without touching the others.

| Scope | Allows |
|---|---|
| `channel` | evaluate, redeem, reverse, offline import, snapshot |
| `admin` | everything under management: campaigns, product lists, webhooks, API keys |

For local development and demos, organizations and keys can be seeded from configuration (`Seed`
section; see `appsettings.Development.json`).

## 3. Channel flow (POS, web shop, mobile app)

```
 basket changes ──► POST /evaluate            (stateless, as often as needed)
 payment OK     ──► POST /redemptions         (transactionId = receipt / order number)
 void / return  ──► POST /redemptions/{id}/reverse
```

* Build the cart from the channel's data: `lineId`, `sku`, `quantity`, `unitPrice`, `categories`
  (full path), `brand`, `attributes`, `minimumUnitPrice`, `discountable`.
* Show `appliedCampaigns[].displayMessage` and `hints` ("add 40 TRY for free shipping").
* Print `lines[].discounts` on the receipt; store them with the sale for returns.
* `POST /redemptions` re-evaluates the cart — the channel cannot claim a discount the engine would not
  give. If limits ran out between evaluate and redeem, the redemption reflects that; compare
  `totalDiscount` with what you showed the customer.
* Retrying `POST /redemptions` with the same `transactionId` is always safe.

### Partial returns

v0.1 reverses whole transactions. For a partial return, refund each returned line's own allocated
discount (`lines[].discounts`), then — if your policy requires re-pricing the remaining items — call
`/evaluate` with the remaining lines and settle the difference in the channel.

## 4. Stores with unreliable connectivity (local mode)

1. At start-up and every few minutes, `GET /snapshot` with `If-None-Match` (304 when unchanged).
   Subscribe a webhook to refresh immediately after changes.
2. Evaluate locally. .NET POS: `LocalCampaignEngine` from `CampaignEngine.Client`. Other languages:
   call a small local proxy (a .NET process with the Client library) or port the rules you need.
3. When back online, report each sale with `POST /redemptions/offline`.

Usage limits and budgets cannot be enforced while offline. Give such campaigns a
`maxDiscountPerOrder` and accept that a budget can be exceeded by offline sales.

## 5. E-commerce platforms

Platforms such as Akinon offer a *basket offer* hook that sends the basket to an external service and
applies the returned discounts. Map it to `POST /evaluate`:

| Platform basket | Campaign Engine cart |
|---|---|
| basket item id | `lineId` |
| product sku | `sku` |
| unit price, quantity | `unitPrice`, `quantity` |
| product attributes | `attributes`, `brand`, `categories` |
| voucher code | `couponCodes` |
| payment option / card | `payment` |
| shipping amount | `shippingAmount` |

Map `appliedCampaigns[].lines` back to the discounted basket items. After order creation call
`POST /redemptions` with the order number. Platforms without such a hook can use the engine from
their checkout customization layer, or receive campaigns as fixed prices exported from the engine.

## 6. ERP and back-office

* Maintain product lists from ERP exports: `PUT /product-lists/{code}/skus/import` with a CSV.
  Typical lists: `REGULATED` (kind `globalExclusion`), per-season assortments, brand exclusions.
* Put the ERP campaign number in `metadata`; it is copied into every evaluation result.
* Accounting can read the ledger (`transactions`, `redemptions` tables) or subscribe to
  webhooks. Budget consumption per campaign is in `campaign_usage`.

## 7. Operations

* Database: SQLite for a single instance / evaluation; SQL Server or PostgreSQL for production
  (`Database:Provider`). The schema is created on first start (`Database:AutoCreate`).
* Several API instances can share a database. Each caches the catalog for `Catalog:CacheSeconds`;
  redemption limits are safe across instances (optimistic concurrency on usage counters).
* Put the API behind TLS; the API key travels in a header.
* Webhook URLs are configured by admins only; the dispatcher will call whatever URL is registered.
