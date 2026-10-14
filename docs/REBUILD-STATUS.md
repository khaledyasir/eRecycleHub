# Rebuild status

Resume point for anyone continuing the work. Newest entries first. The full account is in `REBUILD-REPORT.md`.

## Current state (2026-10-14): feature-complete against the spec; owner decisions and production steps remain

### Done and verified
- Backend: all services and endpoints; EF Core migration; dev seed; legacy import tool with dry-run.
  `dotnet test` -> 49 unit + 147 integration passing (real SQL Server, throw-away database).
- Web: public site, customer account, publisher portal, admin panel (dashboard, reports, winners, customers, publishers, applications,
  wallet transactions, audit, and all CMS pages incl. translations), English and Arabic (RTL).
  `tsc`, `eslint` clean; production `next build` succeeds; Vitest 46 passing; Playwright 36 desktop + 22 phone passing (against the production build).
- Docs: LEGACY-AUDIT, FEATURE-INVENTORY, PAGE-ROUTE-MATRIX, DATABASE-MAPPING, SECURITY-REVIEW, REBUILD-PLAN, REBUILD-REPORT.
- Scripts (`scripts/`), env examples, deployment files (`infra/`, not executed here).

### Legacy project
Removed from the working tree on 2026-10-14 (still in git: tag `legacy-before-removal`); untracked uploads and config were copied to
`C:\Users\khale\Desktop\sites\GIT\legacy-local-files-backup` first. The deletions are not committed. See REBUILD-REPORT section 14.

### Not done / owner actions (see REBUILD-REPORT section 12)
- No import has been run against real legacy data; do a dry run on a restored copy first and review `import-report.md`.
- `infra/DEPLOYMENT.md` (Windows/Node, no containers) was written from the code and not executed on a server.
- Manual accessibility audit with assistive technology; automated axe checks only.
- Business sign-off on the decisions listed in `REBUILD-PLAN.md`; rotate the secrets that remain in the legacy repository history.
- A real e-mail provider key and production SQL login must be supplied by the owner (never committed).

### Environment facts
- .NET SDK 10.0.401, Node 24.19, SQL Server 2025 Developer at `localhost` (Windows auth). Legacy dev DB `ErecycleHubDev` is a read-only reference;
  the new development database is `ERecycleHub_Dev`.
- E2E needs the API started with relaxed rate limits (`./scripts/dev.ps1 -RelaxedRateLimits`) - the normal 20 sign-ins/minute limit
  correctly returns 429 to the test runner.
