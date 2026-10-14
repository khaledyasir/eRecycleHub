# Tests

Each suite lives next to the code it tests; run them all with `../scripts/test.ps1` (add `-E2E` for Playwright).

| Suite | Location | Runs with | Needs |
|---|---|---|---|
| Backend unit | `apps/api/tests/ERecycleHub.UnitTests` | `dotnet test apps/api/ERecycleHub.slnx` | nothing |
| Backend integration (bidding concurrency, wallet, security, workflows, import) | `apps/api/tests/ERecycleHub.IntegrationTests` | same | local SQL Server; creates and drops its own `ERecycleHub_Test_*` database |
| Web unit / component | `apps/web/src/**/*.test.ts(x)` | `npm test` in `apps/web` | nothing |
| End to end (+ axe accessibility checks, phone viewport) | `apps/web/e2e` | `npx playwright test` in `apps/web` | API (relaxed rate limits) + web running on a seeded **development** database |

Results of the last full run are recorded in `docs/REBUILD-REPORT.md` section 2.
