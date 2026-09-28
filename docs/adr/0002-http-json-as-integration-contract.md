# ADR 0002 — HTTP + JSON as the integration contract

**Status:** accepted

## Context

Clients include a Delphi POS (possibly an old Delphi version), Python services, .NET services and a
SaaS e-commerce platform. gRPC, message brokers and client SDKs each exclude at least one of them.

## Decision

* The contract is REST-style HTTP with JSON bodies, described by an OpenAPI document.
* Names are `camelCase`, enums are strings, money is a JSON number with at most 4 decimals.
* Polymorphic objects use a `"type"` discriminator that may appear anywhere in the object
  (hand-written JSON in Delphi or Python does not guarantee property order).
* Change notifications are plain HTTP webhooks (outbox pattern), so no broker is required.
  A broker adapter can be added later behind the same outbox.

## Consequences

* Any language with an HTTP client can integrate in an afternoon.
* High-volume POS integrations can switch to local evaluation with the snapshot endpoint.
