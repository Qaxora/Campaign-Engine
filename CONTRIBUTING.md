# Contributing

Thanks for helping! Issues and pull requests are welcome.

## Build and test

```bash
dotnet build
dotnet test
dotnet run --project src/CampaignEngine.Api   # http://localhost:5080/docs
```

Requires the .NET SDK pinned in [global.json](global.json).

## Guidelines

* **Core stays pure.** `CampaignEngine.Core` has no I/O and no dependencies beyond the BCL. Anything
  that talks to a database, the network or the clock belongs in Infrastructure or the API.
* **Money is `decimal`.** Never use `double`/`float` for amounts. Round with `Money.Round`, split with
  `Money.Allocate`.
* **Rewards change prices only through `RewardContext`**, which enforces floors, caps and rounding.
* **The JSON contract is public.** Adding optional fields is fine; renaming or removing fields is a
  breaking change and needs a new API version.
* **Tests first for engine changes.** Every rule type and every bug fix gets a unit test in
  `tests/CampaignEngine.Core.Tests`; HTTP behaviour goes in `tests/CampaignEngine.Api.Tests`.
* Warnings are errors. Follow `.editorconfig`.
* One logical change per commit, with a conventional prefix (`feat:`, `fix:`, `docs:`, `test:`, …).

## Design decisions

Record significant decisions as a new file in [docs/adr](docs/adr).
