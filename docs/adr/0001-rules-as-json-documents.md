# ADR 0001 — Store campaign rules as JSON documents

**Status:** accepted

## Context

Campaign rules are polymorphic trees (conditions, composites, rewards). Mapping each rule type to
its own table makes every new rule type a schema migration, and every read a multi-table join.
The legacy system this project replaces suffered exactly from that: rules were spread across tables
and people edited them with SQL.

## Decision

A campaign is persisted as one row. Columns that are queried or indexed (`code`, `status`,
`startsAt`, `endsAt`, `priority`, `version`, `updatedAt`) are real columns. The full definition is a
JSON column produced by the same serializer that the HTTP API uses.

Large product lists are the exception: they live in their own tables (`product_lists`,
`product_list_items`) because they can hold tens of thousands of SKUs and are edited independently.
Campaigns reference them by code.

## Consequences

* Adding a rule type needs no migration.
* The stored JSON is exactly what the API accepts and returns — what you see is what is stored.
* Querying *inside* rules with SQL is intentionally inconvenient. Use the API or the conflict
  analyzer instead.
