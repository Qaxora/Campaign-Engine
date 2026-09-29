# Rule reference

Every campaign has one **reward**, any number of **conditions** (all must hold) and a **target**
product selector. Polymorphic objects carry a `"type"` field; it may appear anywhere in the object.
Numbers may be sent as JSON numbers or strings.

## Campaign fields

| Field | Default | Meaning |
|---|---|---|
| `code` | — (required) | Unique, case-insensitive. Letters, digits, `-`, `_`, `.`; max 64. |
| `name` | — (required) | Human-readable name. |
| `description`, `displayMessage` | | `displayMessage` is returned with applied campaigns and hints (receipt / basket text). |
| `status` | `draft` | `draft` → `active` ⇄ `paused` → `archived`. Changed only through the lifecycle endpoints. |
| `priority` | `0` | Higher is applied first and wins ties. |
| `stacking` | `stackable` | `stackable` or `exclusive` (never combined; competes with all stackables together). |
| `exclusivityGroup` | `null` | At most one campaign per group applies: the one with the largest discount. |
| `currency` | `null` | Only for carts in this currency. |
| `schedule` | always | See below. |
| `channels` | `[]` = all | Free text agreed between systems: `store`, `web`, `mobile`, `callCenter`, … |
| `stores` | all | `{ "include": [...], "exclude": [...] }` |
| `customerSegments` | `[]` = everyone | Customer must be in one of them. |
| `coupon` | `null` | `{ "codes": ["A","B"], "maxUsesPerCode": 1 }` — one of the codes must be in the cart. |
| `target` | all products | Product selector the reward applies to. |
| `conditions` | `[]` | All must hold. |
| `reward` | — (required) | Exactly one. |
| `limits` | none | `maxRedemptions`, `maxRedemptionsPerCustomer`, `budget`, `maxDiscountPerOrder`. |
| `ignoreGlobalExclusions` | `false` | Allow discounting products in `globalExclusion` lists. |
| `tags`, `metadata` | | Free-form. `metadata` is copied to evaluation results (e.g. the ERP campaign number). |

### Schedule

```json
{
  "startsAt": "2026-06-01T00:00:00+03:00",   // inclusive, optional
  "endsAt":   "2026-09-01T00:00:00+03:00",   // exclusive, optional
  "timeZone": "Europe/Istanbul",             // IANA or Windows id, default UTC
  "daysOfWeek": ["saturday", "sunday"],      // optional
  "dailyStart": "10:00", "dailyEnd": "14:00" // optional, may cross midnight (22:00–02:00)
}
```

Days and daily windows are evaluated in `timeZone` at the cart's `timestamp` (or now).

## Product selector

A line matches when it matches **any** include criterion (or there is none) and **no** exclude criterion.
Comparisons are case-insensitive.

```json
{
  "skus": [], "categories": [], "brands": [], "productLists": [],
  "attributes": { "season": ["SS26"] },
  "excludeSkus": [], "excludeCategories": [], "excludeBrands": [], "excludeProductLists": []
}
```

Channels should send the full category path on each cart line so that selectors can target any level.

Products in any list of kind `globalExclusion`, and cart lines with `"discountable": false`, are never
discounted (unless `ignoreGlobalExclusions`).

## Conditions

Conditions look at the cart as sent (list prices), never at discounts from other campaigns. Threshold
conditions count the campaign's **qualifying lines** (target ∩ discountable ∩ not globally excluded)
unless they carry their own `products` selector.

| `type` | Fields | Example |
|---|---|---|
| `minSubtotal` | `amount`, `products?` | "500 and above" — produces an upsell hint with `missingAmount` |
| `minQuantity` | `quantity`, `products?` | "at least 3 items" — hint with `missingQuantity` |
| `firstOrder` | | Customer's first order (`customer.isFirstOrder`) |
| `payment` | `methods[]`, `bankCodes[]`, `cardBins[]` (prefixes), `minInstallments?`, `maxInstallments?` | Bank card campaigns |
| `cartAttribute` | `key`, `values[]` | `deliveryType = clickAndCollect` |
| `allOf` | `conditions[]` | |
| `anyOf` | `conditions[]` | |
| `not` | `condition` | |

## Rewards

| `type` | Fields | Notes |
|---|---|---|
| `percentageDiscount` | `percent`, `maxDiscount?` | On the current net amount of target lines. |
| `amountDiscount` | `amount`, `perUnit` | Per order (spread over lines by net amount) or per unit. |
| `fixedUnitPrice` | `price` | "Everything for 99.90". Locks the units. |
| `buyXGetY` | `buyQuantity`, `getQuantity`, `discountPercent` (100), `getProducts?`, `maxApplications?` | "3 for 2" = 2+1; "2nd item half price" = 1+1 at 50%. Units sorted most → least expensive, grouped in sets of X+Y, cheapest Y discounted. With `getProducts`, the Y cheapest units come from that selector. |
| `bundlePrice` | `quantity`, `price`, `maxApplications?` | "Any 3 for 100". Sets built from the most expensive units. |
| `tieredDiscount` | `basis` (`subtotal`/`quantity`), `tiers[]` of `{threshold, percent? \| amount?, maxDiscount?}` | Highest reached tier; hints the next tier. |
| `freeShipping` | `maxAmount?` | Discounts `cart.shippingAmount`. |
| `giftProduct` | `sku`, `quantity` | Reported in `gifts`; the channel adds the free line. |

### How rewards interact

* Campaigns apply in priority order on a working copy of the cart.
* **Proportional** rewards (percentage, amount, tiered) discount what is left after earlier campaigns —
  10% then 20% on 100 gives 28, not 30.
* **Unit-based** rewards (`buyXGetY`, `bundlePrice`, `fixedUnitPrice`) *lock* the units they use: a unit
  is never part of two unit offers.
* No unit goes below the line's `minimumUnitPrice`.
* `limits.maxDiscountPerOrder` and the remaining `limits.budget` cap the campaign's discount per cart.
* Weighed lines (non-integer quantity) are skipped by `buyXGetY` and `bundlePrice`.

## Evaluation result (abridged)

```jsonc
{
  "subtotal": 899.70, "lineDiscount": 299.90, "shippingDiscount": 0, "totalDiscount": 299.90, "total": 599.80,
  "appliedCampaigns": [
    { "code": "SS26-TSHIRT-3X2", "rewardType": "buyXGetY", "discount": 299.90,
      "lines": [ { "lineId": "1", "discount": 299.90 } ], "metadata": { "erpCampaignNo": "2026-0042" } }
  ],
  "lines": [ { "lineId": "1", "gross": 899.70, "discount": 299.90, "net": 599.80,
               "discounts": [ { "campaignCode": "SS26-TSHIRT-3X2", "amount": 299.90 } ] } ],
  "gifts": [], "hints": [ { "code": "SHIP-500", "missingAmount": 0.40 } ],
  "unusedCouponCodes": [],
  "rejections": [ { "code": "WEB-40", "reason": "channelMismatch" } ]   // only with explain=true
}
```

Rejection reasons: `notActive`, `currencyMismatch`, `outsideSchedule`, `channelMismatch`,
`storeMismatch`, `segmentMismatch`, `couponMissing`, `couponExhausted`, `usageLimitReached`,
`customerLimitReached`, `budgetExhausted`, `noEligibleItems`, `conditionNotMet`,
`lostInExclusivityGroup`, `notSelected`, `noDiscount`, `invalidDefinition`.

## Adding a rule type

1. Create a class deriving from `Condition` or `Reward` in `src/CampaignEngine.Core/Rules/...`.
2. Register it with `[JsonDerivedType(typeof(MyRule), "myRule")]` on the base class.
3. Implement `Evaluate` / `Apply` (rewards must change prices only through `RewardContext`) and `Validate`.
4. Add tests in `tests/CampaignEngine.Core.Tests` and a row to this file.

## Plain-English descriptions

`GET /api/v1/campaigns/{idOrCode}/description` (and `POST /api/v1/campaigns/describe` for an unsaved
definition) returns the campaign described by the engine itself — summary, reward, conditions,
products, audience, schedule, limits and combination. The web app, e-mails and the AI assistant's
fallback all use this text, so no client has to interpret rule types. When you add a rule type, add
its sentence to `CampaignDescriber` and a case to `DescriberTests`.
