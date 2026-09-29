# Qaxora Campaign — web app

The SaaS console served at `campaign.qaxora.com`: Next.js 16 (App Router), TypeScript, Tailwind CSS 4.

## Run locally

```bash
# 1. API (from the repository root) — SQLite, seeded demo organization
dotnet run --project src/CampaignEngine.Api          # http://localhost:5080

# 2. Web
cd web
cp .env.example .env.local                           # CAMPAIGN_API_URL=http://localhost:5080
pnpm install
pnpm dev                                             # http://localhost:3000 → Create an account
```

## How it talks to the API (ADR 0007)

```
browser ──► Next.js (same origin)                      ──► Campaign API
            /api/session/login|register|logout             /api/v1/auth/*      token kept in an httpOnly cookie
            /api/v1/*  (proxy, adds Authorization)         /api/v1/*
            server components (serverApi())                /api/v1/*
```

* The session token never reaches browser JavaScript. `qxc_session` is `httpOnly`, `SameSite=Lax`,
  `Secure` in production; the proxy also refuses cross-origin state-changing requests.
* `src/proxy.ts` only checks that a cookie exists; the API decides whether it is valid. An expired
  session is cleared through `/api/session/end`.
* No business rules live here. Pricing, validation, conflicts and permissions come from the API; the
  UI renders what it returns.

## Typed API client

`openapi.json` is a snapshot of the API contract, kept current by a .NET test. After changing the API:

```bash
UPDATE_OPENAPI=1 dotnet test --filter OpenApiSnapshot    # repository root
pnpm gen:api                                             # web/ → src/lib/api/schema.d.ts
```

Use `serverApi()` in server components and `api` from `@/lib/api/client` in client components; both
are `openapi-fetch` clients typed by that schema.

## Scripts

| Script | |
|---|---|
| `pnpm dev` / `build` / `start` | Next.js |
| `pnpm lint` | ESLint |
| `pnpm typecheck` | route types + `tsc --noEmit` |
| `pnpm test` | Vitest unit tests (`src/**/*.test.ts`) |
| `pnpm gen:api` | regenerate API types from `openapi.json` |

CI runs all of them and fails when `schema.d.ts` is stale.
