# Campaign Engine

[![CI](https://github.com/Qaxora/campaign/actions/workflows/ci.yml/badge.svg)](https://github.com/Qaxora/campaign/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A generic, channel-agnostic **campaign / promotion engine** for retail, written in .NET 10.

One engine, one rule model, every channel: the store POS, the web shop, the mobile app and the back
office all ask the same service "what does this basket cost?" and get the same answer — with a
line-by-line explanation. It speaks plain JSON over HTTP, so a Delphi POS, a Python service and a .NET
back office integrate the same way.

## Why

Retailers with several systems (ERP, a legacy back office, POS software, an e-commerce platform)
usually define promotions in each of them. The result is familiar:

* a store campaign and a web campaign on the same products stack into a discount nobody approved;
* "include these products" and "never discount those" lists are maintained with hand-written SQL;
* the same basket costs a different amount in the store and online.

Campaign Engine centralizes the rules, **detects conflicts before a campaign goes live**, replaces SQL
scripts with product-list APIs, and enforces the safety rules (never-discount lists, price floors,
budgets, usage limits) in one place.

## Features

* **Rules** — percentage / amount / fixed price, buy X get Y (incl. "buy a shoe, get socks"), bundle
  price ("any 3 for 100"), tiered spend or quantity, free shipping, gift product.
* **Conditions** — minimum subtotal or quantity, first order, payment (method, bank, card BIN,
  installments), cart attributes, `allOf` / `anyOf` / `not`.
* **Targeting** — channels, stores, customer segments, coupons (incl. single-use), currency, and a
  schedule with time zone, weekdays and happy hours.
* **Combination** — priorities, stackable vs exclusive (best result for the customer wins),
  exclusivity groups, unit locking so a unit is never used by two unit offers.
* **Safety** — global never-discount lists, non-discountable lines, per-unit price floors, per-order
  caps, budgets, total / per-customer / per-coupon limits (safe under concurrency).
* **Explainability** — per-line allocations that add up to the cent, rejection reasons for every
  campaign that did not apply, upsell hints ("add 40 more for free shipping").
* **Conflict analysis** — overlapping campaigns across channels, stores, schedules and product scopes,
  reported on activation (duplicate coupons block it).
* **Ledger** — idempotent redemptions by transaction id, reversals, offline sale import.
* **Distribution** — ETag-cached snapshot for local/offline evaluation, signed webhooks through a
  transactional outbox.
* **Runs anywhere** — SQLite, SQL Server or PostgreSQL; Docker image; OpenAPI 3.1 + Scalar UI.

## Quick start

```bash
git clone https://github.com/Qaxora/campaign.git && cd campaign
dotnet run --project src/CampaignEngine.Api
# → http://localhost:5080/docs   (keys: dev-admin-key / dev-pos-key)
```

or with Docker and PostgreSQL:

```bash
docker compose up --build        # → http://localhost:8080/docs (keys: local-admin-key / local-pos-key)
```

Create a campaign, activate it and price a basket:

```bash
curl -X POST localhost:5080/api/v1/campaigns -H "X-Api-Key: dev-admin-key" -H "Content-Type: application/json" -d '{
  "code": "TSHIRT-3X2", "name": "T-shirts 3 for 2",
  "target": { "categories": ["apparel-tshirt"] },
  "reward": { "type": "buyXGetY", "buyQuantity": 2, "getQuantity": 1 }
}'
curl -X POST localhost:5080/api/v1/campaigns/<id>/activate -H "X-Api-Key: dev-admin-key"

curl -X POST "localhost:5080/api/v1/evaluate?explain=true" -H "X-Api-Key: dev-pos-key" -H "Content-Type: application/json" -d '{
  "channel": "store",
  "lines": [ { "lineId": "1", "sku": "TS-001", "quantity": 3, "unitPrice": 299.90, "categories": ["apparel-tshirt"] } ]
}'
```

More in [samples/http/campaign-engine.http](samples/http/campaign-engine.http).

## Integrating

| From | How |
|---|---|
| .NET | [`CampaignEngine.Client`](src/CampaignEngine.Client) — typed client + `LocalCampaignEngine` for offline mode. Or reference `CampaignEngine.Core` and evaluate in-process. |
| Python | [samples/python](samples/python) — standard library only |
| Delphi | [samples/delphi](samples/delphi) — `System.Net.HttpClient` unit, Indy notes for older versions |
| Anything else | OpenAPI document at `/openapi/v1.json` — generate a client or call it by hand |

Read the [integration guide](docs/integration-guide.md) for the channel flow, offline mode,
e-commerce basket hooks and ERP product-list imports.

## Documentation

* [Architecture](docs/architecture.md) — goals, components, evaluation algorithm, ledger, conflicts
* [Rule reference](docs/rule-reference.md) — every field, condition and reward
* [Integration guide](docs/integration-guide.md)
* [Architecture decisions](docs/adr)

## Project layout

```
src/
  CampaignEngine.Core            rule model, evaluator, conflict analyzer, JSON contract (no dependencies)
  CampaignEngine.Infrastructure  EF Core persistence, catalog cache, ledger, webhooks
  CampaignEngine.Api             ASP.NET Core minimal API
  CampaignEngine.Client          .NET client + local evaluator
tests/
  CampaignEngine.Core.Tests      engine unit tests
  CampaignEngine.Api.Tests       end-to-end tests against the real API
samples/                         HTTP, Python, Delphi
```

## Roadmap

* Partial returns with automatic re-pricing of the remaining items
* Provider-specific EF Core migrations (the schema is currently created on start-up)
* Coupon pools (generate / import thousands of single-use codes)
* Loyalty-point rewards and "spend now, get a coupon for next time"
* Admin UI
* Message-broker adapters for the outbox (RabbitMQ, Kafka, Azure Service Bus)
* Per-line tax information and tax-aware rounding

## Türkçe özet

Perakendede kampanyalar genellikle ERP'de, eski back-office yazılımında, mağaza kasasında ve
e-ticaret platformunda ayrı ayrı kurgulanır; kanallar arası çakışmalar, SQL ile yönetilen ürün
listeleri ve mağaza/online fiyat farkları bu yüzden çıkar. **Campaign Engine** kampanyaları tek bir
yerde toplayan, her dilden (Delphi, Python, .NET …) JSON/HTTP ile çağrılabilen bir kampanya
motorudur: sepeti gönderirsiniz, satır bazında indirimleri ve nedenlerini alırsınız. Kampanya
yayına alınmadan önce diğer kampanyalarla çakışmaları raporlanır; yasaklı ürün listeleri, taban
fiyatlar, bütçe ve kullanım limitleri motor tarafından uygulanır. Mağaza bağlantısı koptuğunda
snapshot ile yerel (offline) hesaplama yapılabilir.

## License

[MIT](LICENSE)
