# ADR 0006 — The AI assistant proposes; the engine decides

**Status:** accepted

## Context

Writing campaign definitions is the slowest part of campaign operations, and natural language
("20% off above 1000 TRY, except electronics, in October") is how merchandisers think. Language models
are good at interpretation and bad at arithmetic, consistency and accountability.

## Decision

* The assistant sits behind `IAiCampaignAssistant`; the default implementation calls Ollama
  (`llama3.1:8b`), configured through `OLLAMA_BASE_URL` and `OLLAMA_MODEL`. Other providers implement
  the same interface.
* The model fills a small, schema-constrained **intent** object. C# code maps the intent to a
  campaign definition and resolves names against the tenant's catalog. The model never writes the
  campaign JSON directly.
* Every proposal goes through `CampaignValidator` and `ConflictAnalyzer`. The response always carries
  the deterministic results; the model may only rephrase them.
* A proposal is not persisted. Saving creates a **draft**; activation is a separate human action with
  the normal conflict checks. There is no endpoint through which the AI can activate, price, redeem or
  authorize.
* Explanations are grounded in data computed by the backend and fall back to templates when the
  provider is unavailable.

## Consequences

* A wrong interpretation costs a human review, never money.
* The assistant can be swapped or moved to a separate process without touching the engine.
* The quality of proposals depends on the catalog: categories and brands the tenant has registered
  are recognised; others are reported as unresolved.
