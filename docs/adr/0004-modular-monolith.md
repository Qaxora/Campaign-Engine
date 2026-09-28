# ADR 0004 — Modular monolith for the SaaS platform

**Status:** accepted

## Context

The platform adds organizations, identity, catalog, ledger queries, analytics, audit, integrations and
an AI assistant around the existing engine. The team is small, the load profile is unknown, and every
module needs the same tenant boundary and the same database transaction semantics (for example a
campaign change and its outbox message must commit together).

## Decision

One API process and one database. Modules live in separate folders/namespaces with their own services
and records; they talk through services, not by reaching into each other's tables. The AI module is a
separate project with no persistence dependency so it can move to its own process later.
`CampaignEngine.Core` stays pure and unchanged in spirit.

## Consequences

* Simple deployment, local transactions, one migration history.
* Module boundaries are a convention enforced by review, not by the network.
* Splitting a module later means putting an HTTP/queue adapter behind its existing service interface.
