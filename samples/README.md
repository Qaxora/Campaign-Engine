# Integration samples

The API is plain JSON over HTTP, so any language works. These samples show the typical channel flow:
**evaluate** the basket while the customer shops → **redeem** when the sale completes → **reverse** on
void/return.

| Folder | What | Requirements |
|---|---|---|
| [http](http/campaign-engine.http) | Every important call as a request collection | VS Code REST Client, Rider or Visual Studio |
| [python](python) | `campaign_client.py` (client) and `webhook_receiver.py` (signature check) | Python 3.9+, standard library only |
| [delphi](delphi) | `CampaignEngine.Client.pas` unit and `PosDemo.dpr` console POS | Delphi XE8+ (Indy notes for older versions inside) |
| .NET | Use the [`CampaignEngine.Client`](../src/CampaignEngine.Client) project | .NET 10 |

Start a local API first:

```bash
dotnet run --project src/CampaignEngine.Api   # http://localhost:5080, docs at /docs
```

Development keys: `dev-admin-key` (manage campaigns) and `dev-pos-key` (channel).

> The .NET client is covered by the automated tests. The Python and Delphi samples are reference
> code and are not compiled in CI; please open an issue if something does not work with your version.

## Things every integration should do

* **Send the full category path** on each line (`["apparel", "apparel-tshirt"]`) so campaigns can
  target any level of the hierarchy.
* **Send `minimumUnitPrice`** when the product has a price floor and **`discountable: false`** for
  items that must never be discounted.
* **Use a stable `transactionId`** (receipt number, order number). Redemption is idempotent on it, so
  retry freely after timeouts.
* **Print the per-line allocations** (`lines[].discounts`) on receipts and keep them: returns are
  refunded against them.
* **Write decimals with `.`** regardless of the OS locale. Numbers may also be sent as strings
  (`"199.90"`), which is the safest option for languages without a decimal type.
