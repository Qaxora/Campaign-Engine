# ADR 0007 — Next.js as a backend-for-frontend with opaque sessions

**Status:** accepted

## Context

The web app needs sign-in, organization switching and calls to the same API that integrations use.
Keeping tokens in browser storage exposes them to XSS; duplicating API logic in the web app would
fork the rules.

## Decision

* The API issues **opaque session tokens** on login (random 256-bit, only the SHA-256 hash is stored,
  fixed expiry, revocable on logout).
* The Next.js server keeps the token in an `httpOnly`, `Secure`, `SameSite=Lax` cookie and forwards
  requests to the API with `Authorization: Bearer`. Browser code talks only to the Next.js origin.
* The API remains the single place for validation, authorization and business rules. Types for the
  web client are generated from the API's OpenAPI document.

## Consequences

* No tokens in JavaScript-readable storage; CSRF is limited by `SameSite` and same-origin requests.
* Every web request makes one extra hop (browser → Next.js → API), acceptable for a B2B console.
* Integrations are unaffected: they keep using API keys.
