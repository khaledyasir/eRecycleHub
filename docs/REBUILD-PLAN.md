# Rebuild plan

## Architecture (decided)

* **Modular monolith API**: ASP.NET Core 10 minimal APIs (`ERecycleHub.Api`) → application services (`ERecycleHub.Application`)
  → EF Core 10 `AppDbContext` (`ERecycleHub.Infrastructure`) → entities and pure rules (`ERecycleHub.Domain`). Services use the
  DbContext directly (no repository layer: EF already is one). No CQRS/mediator, no microservices.
* **Frontend**: Next.js 16 App Router, React 19, TypeScript strict, Tailwind CSS 4, Radix primitives in shadcn-style local components,
  next-intl (`/en`, `/ar`, RTL), TanStack Query for client mutations/refetch, React Hook Form + Zod, Lucide icons. Public pages are
  server-rendered (SEO); interactive islands are client components.
* **Same origin**: the browser only talks to Next.js. `/api/*` is rewritten to the API; server components call the API directly over the
  internal URL and forward the visitor's cookies. Auth = HttpOnly cookie issued by the API; CSRF = antiforgery header.
* **Contracts**: OpenAPI document generated at build (`apps/api/.../ERecycleHub.Api.json`) → `openapi-typescript` types in
  `apps/web/src/lib/api/schema.d.ts`.
* **Database**: SQL Server, EF Core migrations; separate import tool for legacy data.

## Phases and acceptance criteria

| # | Phase | Done when |
|---|---|---|
| 1 | Discovery | The 6 docs exist; business rules and defects recorded (this folder). |
| 2 | Foundation | API + web build; API serves OpenAPI; web renders `/en` and `/ar`; proxy + cookie + CSRF round-trip verified. |
| 3 | Domain & DB | Migrations apply to an empty SQL Server DB; dev seed runs; unit tests for rules; integration tests for bid concurrency & wallet idempotency pass. |
| 4 | Public site | Every public route in the matrix renders from real API data in both languages; filters/pagination server-side. |
| 5 | Customer | Account pages work end-to-end (bid, favourite, watch, review, wallet request, notifications, settings, password). |
| 6 | Publisher | All seller pages work; ownership integration tests (IDOR) pass. |
| 7 | Admin | All admin pages work; approval/reversal flows tested. |
| 8 | Localisation & polish | No hard-coded UI strings; RTL verified; responsive at 360/768/1280/1920; keyboard + focus + reduced motion. |
| 9 | Verification | dotnet test, vitest, Playwright (roles, IDOR, i18n, responsive, console/network errors) all green. |
| 10 | Docs & audit | `REBUILD-REPORT.md` with parity matrix, test evidence, limitations. |

## Decisions recorded (ambiguities resolved without business sign-off)

1. Insurance amount stays an eligibility check (wallet ≥ insurance); nothing is held or charged.
2. Admins cannot bid (conflict of interest); publishers may bid on other publishers' auctions (legacy allowed it).
3. Bid amounts with more than two decimals are rejected (legacy rounded silently).
4. After the first bid: start price, increment, insurance and start time are frozen; the end time may move but not into the past.
5. Approved credits are reversed (ledger entry), never deleted; only pending/rejected requests can be deleted.
6. Reviews: one per account per auction, rating 1–5 required.
7. Clients page shows admin-flagged customers only.
8. Category becomes an optional property of an auction; legacy auctions are uncategorised.
9. Default language English (legacy Web.config culture), Arabic fully supported; currency JOD with 2 decimals (the legacy schema's precision).
10. Brand "eRecycleHub"; TenderJO kept as the footer design credit.
11. Customer company/bank/KYC fields are optional on sign-up (legacy made them required for all customers, which blocks individuals); admins see which documents are missing.
