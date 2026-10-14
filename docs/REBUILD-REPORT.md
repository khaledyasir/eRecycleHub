# eRecycleHub rebuild — final report

Date: 2026-10-14. The rebuild was built in `rebuild/` while the original project stayed read-only. At the owner's request the original sources were then
removed from the working tree (section 14); they remain in git history.

Statements below are tied to something that was run. Where something was **not** run or measured, it says so (section 12).
Nothing here claims the system is bug-free or perfectly secure.

## 1. Summary

The legacy ASP.NET MVC 5 / EF6 auction platform was rebuilt from scratch as:

* **API** — ASP.NET Core 10, EF Core 10, SQL Server (modular monolith, minimal APIs, cookie auth, antiforgery, rate limiting).
* **Web** — Next.js 16 (App Router), TypeScript strict, Tailwind CSS 4, next-intl (English + Arabic RTL), Radix primitives, Lucide icons.
* **Roles** — Admin, Publisher (store portal), Customer, with the legacy public site, customer account, publisher portal and admin dashboard.
* **Money and bidding** — decimal money, UTC times, atomic bidding, a managed wallet with approval workflow and an append-only ledger.
  Insurance is an eligibility check only: it is never charged, held or deducted.
* **Migration tool** — a separate, read-only-on-source import tool with dry-run, plus a mapping document.

## 2. What was verified, in one table

| Check | Command (from `rebuild/`) | Result on 2026-10-14 |
|---|---|---|
| Backend unit tests | `dotnet test apps/api/ERecycleHub.slnx` | 49 passed |
| Backend integration tests (real SQL Server, throw-away DB) | same command | 147 passed, 0 failed (≈ 1 min 14 s) |
| Web typecheck | `npm run typecheck` (in `apps/web`) | no errors |
| Web lint | `npm run lint` | no errors, no warnings |
| Web unit / component tests | `npm test` | 5 files, 46 passed |
| Production build | `npm run build` | succeeds (126 pages generated; routes are rendered on demand) |
| End-to-end, desktop Chrome, against the production build | `npx playwright test --project=desktop` | 36 passed |
| End-to-end, phone viewport (Pixel 7) | `npx playwright test --project=phone` | 22 passed |

The E2E run needs the API started with relaxed rate limits (`./scripts/dev.ps1 -RelaxedRateLimits`): the real limit (20 sign-ins per
minute per IP) correctly answered 429 to the test runner during development.

## 3. Architecture

```
Browser ── same origin ──> Next.js (apps/web) ── /api/* rewrite ──> ASP.NET Core API (apps/api) ──> SQL Server
                              │  server components call the API directly,
                              │  forwarding the visitor's cookie / language / IP
```

* **Backend layers** — `Domain` (entities, pure `AuctionRules`), `Infrastructure` (`AppDbContext`, files, e-mail transports),
  `Application` (services using the DbContext directly; no repository layer), `Api` (minimal-API endpoints, security, errors).
* **Frontend** — server components fetch data (`serverApi()` + `load()`); client components mutate through a typed
  `openapi-fetch` client generated from the API's OpenAPI document. Portal layouts guard by role (UX only; the API authorises every call).
* **Errors** — RFC 7807 problem responses carrying a stable `code` (+ args); the web maps codes to localised sentences.
* **Time and money** — UTC in the database and API; display in `Asia/Amman` (configurable) on server and client alike; `decimal(18,2)`;
  money shown with 2 decimals and Latin digits in both languages.
* **Files** — stored outside the web root, served via `/api/files/{id}` with ownership checks; private files return 404 to others.

Key correctness mechanisms:

| Concern | Mechanism | Evidence |
|---|---|---|
| Concurrent bids | `SELECT … WITH (UPDLOCK, ROWLOCK)` on the auction row inside a transaction; the clock is read after the lock | integration tests: equal concurrent bids, a 40-bid race, expiry boundary, injected DB failure rolls back |
| Wallet approval exactly once | conditional `UPDATE … WHERE Status = Pending`, atomic balance `UPDATE … OUTPUT`, ledger with unique (transaction, reason) | integration test: concurrent approvals credit once; E2E: second approve is refused and the balance is unchanged |
| Duplicate top-up requests | idempotency key with a unique filtered index | integration test + E2E (same key twice → one request) |
| Bid never charges the wallet | no balance write in the bid path | E2E: balance equal before and after two bids |

## 4. Technology choices (and the one deviation)

Spec stack used as specified. Notes:

* **next-intl plugin not used.** Its SWC plugin tried to load a native binary whose cache directory has an ACL problem on this machine
  (another local account inherits full control of the profile folders; permissions were not changed). The same behaviour is configured with a
  Turbopack `resolveAlias` in `next.config.ts` and explicit static catalogue imports (`src/i18n/request.ts`). Works in dev and production build.
* **React Hook Form, Zod and `@hookform/resolvers`** are installed but unused: the forms use a small `useForm` helper with explicit validators
  (`src/lib/use-form.ts`). TanStack Query *is* used (live bid polling, session). Remove the three unused packages or adopt them in new work.

## 5. Feature parity

Legend: **Kept** same behaviour · **Fixed** same purpose, legacy bug corrected · **Changed** deliberate difference (see `REBUILD-PLAN.md` decisions) · **Dropped** dead legacy UI.
Details per feature are in `FEATURE-INVENTORY.md`; per route in `PAGE-ROUTE-MATRIX.md`.

| Area | Status | Notes |
|---|---|---|
| Public site: home, auctions list + filters, auction detail, stores, store detail, clients, benefits, about, how to buy/sell, FAQ, support, contact, advertise, terms, privacy | Kept / Fixed | category filter now real; documents inline; bid history without identities; sitemap + robots |
| Country and language selection | Kept | language is a URL prefix (`/en`, `/ar`) |
| Auth: login, register (+KYC fields, optional), forgot/reset password, publisher application | Kept / Fixed | lockout, hashed single-use reset tokens, no account enumeration |
| Customer account: dashboard, my auctions, calendar, wallet (+ledger, proof upload), favourites, notify-me, notifications, settings, password | Kept / Fixed | real bid status (leading/outbid/won/lost) |
| Bidding | Kept | same rules; Changed: admins cannot bid; more than 2 decimals rejected |
| Publisher portal: dashboard, auctions CRUD (+images, documents, specs, Arabic fields), bids, reviews, bidders, store profile, password | Kept / Fixed | ownership enforced in every query (legacy IDOR removed) |
| Admin: dashboard (real 30-day chart), reports (+CSV), winners (notify once), customers, publishers, applications, wallet transactions (approve/reject/reverse/proof), audit log | Kept / Fixed | "delete approved credit" became **reverse** |
| Admin CMS: categories, countries, slides, announcements, about, benefits, how-to, FAQ, contact info, messages, settings pages, translations | Kept | translations are database overrides on top of the shipped catalogues (legacy edited `.resx` files) |
| Payment gateways | not built | by design; the managed wallet/top-up approval workflow is kept, nothing invented |
| Dropped | – | newsletter form with no action, duplicate Suppliers page, fake SKU pane, menu-permission tables with no UI |

Pages: 69 `page.tsx` routes under `src/app/[locale]`. Every legacy URL in the route matrix redirects with 308 (`src/proxy.ts`; covered by unit tests and E2E).

## 6. Data migration

* Tool: `apps/api/tools/ERecycleHub.LegacyImport`, wrapper `scripts/import-legacy.ps1` (**dry run by default**).
* Source is opened read-only; the target must be an empty, migrated database or the tool refuses.
* Conversions (all counted and listed in the generated `import-report.md`): Central time → UTC; `float` money → `decimal(18,2)` with
  `money-adjustments.csv` for every value that changed; legacy `v1` PBKDF2 hashes kept and upgraded on first login; plaintext passwords hashed
  during import (never accepted at runtime); duplicate e-mails neutralised and reported; uploaded files copied and magic-byte validated;
  opening-balance ledger entries so every wallet balance is explained; settings 3/5 → `privacy-policy` / `terms`.
* Evidence: 2 integration tests run the import end to end on synthetic legacy-shaped data. **It has not been run on real legacy data.**
  Do a dry run on a restored *copy* first and read the report. Field-level mapping: `DATABASE-MAPPING.md`.

## 7. Security

Full table in `SECURITY-REVIEW.md`. Implemented and exercised by tests:

* Authentication: HttpOnly `SameSite=Lax` cookie validated against the database on every request (security stamp, role, publisher link) —
  password change/reset or deactivation ends sessions immediately; persistent lockout (5 failures → 15 min); ASP.NET Identity v3 hasher.
* CSRF: antiforgery token (`X-CSRF-TOKEN`) required on every unsafe `/api` request (E2E: a request without it is refused with `csrf.invalid`).
* Authorisation: authenticated-by-default fallback policy; `admin` / `publisher` / `member` policies; ownership inside every owner-scoped query
  (integration tests substitute foreign ids on every such endpoint; E2E confirms a second publisher gets 404 for another's lot).
* Uploads: extension allow-list, size caps, magic bytes, random names, outside the web root, SVG not accepted, `nosniff`, sandbox CSP.
* HTML from admins/publishers sanitised on write; rendered through one component. CSV export neutralises formula injection.
* Rate limits on auth, forms and bids; security headers and CSP (E2E asserts `nosniff`, CSP present, no `X-Powered-By`).
* No secrets in the repository; `.env.example` files contain placeholders only; e-mail defaults to a local pickup folder (nothing is sent in development or tests).

## 8. Design, localisation, accessibility, performance

* Design: patina green / hi-vis yellow (live and bid only) / rust, IBM Plex Sans + IBM Plex Sans Arabic, tokens in `globals.css`. The legacy logos
  belonged to other brands, so an interim wordmark is used (replace `components/layout/logo.tsx` when the owner supplies a logo).
* English and Arabic RTL: logical CSS properties throughout; a unit test fails if the two catalogues differ in keys or ICU placeholders or if an
  Arabic text was left in English (`src/messages/catalogues.test.ts`); E2E checks `dir="rtl"`, Arabic content and Latin digits for money.
* Accessibility: Radix dialogs/menus (focus trap, escape), labelled controls, error text linked with `aria-describedby`, focus moves to the first invalid
  field, skip link, reduced-motion respected. **Automated** axe checks (WCAG 2.0/2.1/2.2 A/AA tags) pass for serious/critical violations on 8 public pages
  in both languages — they found and led to a fix of a real colour-contrast defect on the "ended" badge. Automated tools find only part of the issues;
  a manual audit with a screen reader has **not** been done, so WCAG 2.2 AA conformance is not claimed.
* Responsive: 22 pages (public, customer, publisher, admin; English and Arabic) checked on a phone viewport for horizontal overflow, console errors and failed requests.
* Performance: **not measured** (no Lighthouse/field data). Design choices: server-rendered pages, no client data fetching for first paint, lazy images,
  one font family pair via `next/font`, no animation libraries. All pages are rendered on demand with `no-store` API fetches — correct for live auction data, but no
  page is statically cached. SEO: per-page titles/descriptions, canonical + `hreflang` alternates, `sitemap.xml`, `robots.txt`, `noindex` on portals; not validated with external tools.

## 9. Testing — evidence and coverage

Run everything: `./scripts/test.ps1` (add `-E2E` with the stack running). Stop the API first; the .NET tests rebuild its DLLs.

**Backend (196 tests).** Unit: bid rules, money edge cases, password hashing and legacy `v1` verification, HTML sanitiser. Integration (real SQL Server, one
throw-away database per run, dropped afterwards — verified none left behind): bidding concurrency and boundaries, wallet approve-once / reverse / idempotency,
failure injection with rollback, lockout, session invalidation, reset tokens, CSRF, IDOR on every owner-scoped endpoint, private file access, CSV injection, CMS
CRUD, workflows (application → publisher, winners notified once), endpoint smoke tests, the import tool.

**Web unit/component (46).** Money/date/time-zone formatting and the `fromLocalInput`/`toLocalInput` round trip, `pick`, message-catalogue parity,
legacy redirects incl. case-sensitivity and no redirect loops, validators (2-decimal money, email, URL scheme), `useForm` (no action while invalid,
values kept and localised message shown on API failure), the generic CMS manager (required fields, number conversion, multipart, error display, delete confirm).

**End to end (58 = 36 desktop + 22 phone)**, run against `next start` of the production build with a real API and database:

| Journey | What it proves |
|---|---|
| Visitor | home / list / detail render without console or network errors; search and status filters; protected pages go to sign-in; API 401 and CSRF refusal; headers; legacy redirects (308); 404 page; Arabic is RTL with Latin digits; language switcher; axe checks on 8 pages; skip link |
| Sign-in and roles | wrong password stays on the page with a message; each role lands in its own area; customer cannot open seller/admin; publisher cannot open admin; API returns 403 across roles; sign-out ends the session |
| **Whole auction lifecycle** | a publisher creates a lot through the real form (with image upload) → a customer bids (terms checkbox, confirm dialog) → below-minimum bid is refused → a second customer outbids → the API shows price 120, 2 bids, first bidder no longer leading → **wallet unchanged** → another publisher gets 404 for the lot → admin sees the lot and 2 bids |
| Wallet | customer requests a top-up in the UI (balance unchanged while pending) → admin approves in the UI → balance rises by exactly the amount → a second approval is refused → one ledger entry; same idempotency key twice → one request |
| Admin content | categories created / validated / edited / deleted in the UI and visible through the public API; a text override saved, served and reset; a contact message appears and is marked read |
| Not-enough-balance and admin-can't-bid | UI explains why bidding is unavailable; the API refuses it too |
| Phone | no horizontal overflow, no console errors on 22 pages |

**Pages without their own browser journey** (covered by the HTTP sweep — every admin/seller/account page returns 200 in both languages with no server-side
errors — by the phone/overflow spec, and by backend integration tests of their endpoints): reports, winners, customers and publishers CRUD, applications,
audit, sliders/announcements/about/benefits/how-to/FAQ/contact-info/settings (they share the CMS component that *is* unit-tested and E2E-tested through categories),
publisher bidders/reviews, favourites/watch/notifications/calendar, forgot/reset password UI, registration UI.

## 10. Running it

Prerequisites: .NET 10 SDK, Node 24, SQL Server (LocalDB/Developer/Express) reachable at `localhost` with Windows authentication.

```powershell
./scripts/setup.ps1 -Seed      # npm ci, build, create ERecycleHub_Dev, load sample data
./scripts/dev.ps1              # API http://localhost:5080  +  web http://localhost:3000
./scripts/test.ps1             # all automated checks (stop the API first)
./scripts/test.ps1 -E2E        # plus Playwright (start ./scripts/dev.ps1 -RelaxedRateLimits first)
./scripts/reset-dev-db.ps1 -Yes   # drop and re-seed ERecycleHub_Dev only
```

Sample accounts (development seed only; the API refuses the seed outside Development/Testing and on a database that already has users):
`admin@test.local` / `AdminPass#2026`, `customer@test.local` and `customer2@test.local` / `CustomerPass#2026`,
`publisher@test.local` and `publisher2@test.local` / `PublisherPass#2026`. Development e-mails are written as HTML files to
`apps/api/src/ERecycleHub.Api/App_Data/mail`. Settings: `apps/api/.env.example`, `apps/web/.env.example`.

## 11. Deployment

No containers are used. `infra/DEPLOYMENT.md` describes a plain Windows/Node deployment (API as a service or IIS, web as a Node service, a reverse proxy in
front). **It was written from the code and not executed on a server** — verify it in staging first. Requirements regardless of host:

* TLS terminates at a reverse proxy in front of the web app. The API must **not** be reachable from the internet: it trusts `X-Forwarded-For` from its caller
  (the Next.js server) for rate limiting and audit IPs.
* Set `ConnectionStrings__Default`, `App__PublicUrl`, `Storage__Root` (back it up with the database), `Email__Provider=SendGrid` + key, from a secret store.
* Apply migrations explicitly (`dotnet ERecycleHub.Api.dll migrate`); the app never migrates or seeds on its own in production, and `seed-dev` refuses to run there.
* Use a least-privilege SQL login for the app. Rate-limit partitions are in memory per instance; add a proxy-level limiter for multiple instances.

## 12. Known limitations and owner actions

1. **Legacy import never run on real data.** Dry-run on a restored copy, read `import-report.md` and `money-adjustments.csv`, then import into an empty database.
2. **Secrets in the legacy repository history** (SendGrid key, SQL password) must be rotated by the owner — nothing in this rebuild changes that history.
3. **Deployment guide not executed on a server.** **Real e-mail sending untested** (Pickup transport only; the SendGrid transport has no live test).
4. **Accessibility** verified automatically only; **performance** not measured; SEO not validated externally.
5. `CardNumber` / `AccountNumber` are stored as plain text as in the legacy system. If these are real card numbers the business is in PCI-DSS scope — remove or encrypt.
6. No malware scanning of uploads (files are type-checked and never served inline as HTML).
7. Business decisions taken without sign-off are listed in `REBUILD-PLAN.md` (admins cannot bid; clients page shows opted-in customers only; reverse instead of delete; …). Please review.
8. Cosmetic items left: home-slide aspect ratio could be wider; some meta lines use middle-dot separators; an interim wordmark instead of a real logo.
9. Unused dependencies (React Hook Form, Zod, `@hookform/resolvers`) are installed — remove them or adopt them (section 4).
10. Everything above is **uncommitted** in the working tree on branch `hardening` (`rebuild/` is untracked); review, then commit on a branch of your choice.

## 13. Repository map

```
rebuild/
  apps/api/        ERecycleHub.slnx · src/{Domain,Infrastructure,Application,Api} · tests/{UnitTests,IntegrationTests} · tools/LegacyImport · openapi/
  apps/web/        src/{app,components,lib,i18n,messages} · e2e/ · vitest + playwright config
  docs/            LEGACY-AUDIT · FEATURE-INVENTORY · PAGE-ROUTE-MATRIX · DATABASE-MAPPING · SECURITY-REVIEW · REBUILD-PLAN · REBUILD-STATUS · this report
  scripts/         setup · dev · test · reset-dev-db · import-legacy (PowerShell)
  infra/           DEPLOYMENT.md (no containers; not executed on a server)
  tests/           pointer to where each test suite lives
```

## 14. Removal of the legacy project (2026-10-14)

The owner asked for the old build's files to be removed. Done:

* **Removed from the working tree:** `Web/`, `Entities/`, `Services/`, `packages/`, the legacy `tests/`, `Database/`, `erecyclehub.sln`, `README.md`,
  `REVIEW-REPORT.md` and the old solution's `.vs/` cache. Kept: `rebuild/`, `.git`, the root `.gitignore`, `.vscode`, `.claude-flow`.
* **Nothing tracked was lost.** All 5,026 files are in git: tag `legacy-before-removal` (= `ef1b903`), branch `hardening` (local only - it was never pushed),
  and `master` (also on the `origin` remote). Restore with `git worktree add ../legacy-checkout legacy-before-removal`, or
  `git restore --source=legacy-before-removal -- <path>`. The deletions are **not committed**: `git status` shows them as unstaged deletions next to the untracked `rebuild/`.
* **Files git could not restore were copied first** to `C:\Users\khale\Desktop\sites\GIT\legacy-local-files-backup\` (outside the repository, verified identical):
  `App_File/` (153 uploaded files, 24 MB: auction/user/publisher images, KYC documents, payment proofs), and the git-ignored `connectionStrings.config` and
  `secrets.config`. The two config files may hold real credentials - rotate them (section 12, item 2) and delete the backup when you no longer need it.
  Use that folder as the legacy-uploads folder for the import (`scripts/import-legacy.ps1 -LegacyFiles <folder that contains App_File>`).
* **Not removed:** the legacy SQL Server database `ErecycleHubDev` (it is data, not files) and the production database/server.
* **One dependency was moved:** two integration tests built a throw-away legacy database from the legacy `Database/schema.sql` and `seed.dev.sql`. Identical
  copies now live in `apps/api/tests/ERecycleHub.IntegrationTests/LegacyFixtures/`. After the removal all 49 unit + 147 integration tests pass again.
