# eRecycleHub — rebuild

Online auctions for recyclable materials. A from-scratch rebuild of the legacy ASP.NET MVC 5 platform:
**ASP.NET Core 10 + EF Core + SQL Server** API and a **Next.js 16** web app, in English and Arabic (RTL).

| Folder | What |
|---|---|
| `apps/api` | API solution: `Domain`, `Infrastructure`, `Application`, `Api`, unit + integration tests, legacy import tool |
| `apps/web` | Next.js app (public site, customer account, publisher portal, admin panel), Vitest + Playwright tests |
| `docs` | Legacy audit, feature inventory, route matrix, database mapping, security review, plan, status and the **final report** |
| `scripts` | PowerShell: setup, dev, test, reset the dev database, legacy import |
| `infra` | Deployment guide without containers (written from the code; not executed on a server) |

Start with [docs/REBUILD-REPORT.md](docs/REBUILD-REPORT.md) (what was built, how it was verified, what is not done).

## Quick start (Windows, SQL Server on localhost)

```powershell
./scripts/setup.ps1 -Seed     # install, build, create ERecycleHub_Dev with sample data
./scripts/dev.ps1             # API http://localhost:5080, web http://localhost:3000
./scripts/test.ps1            # unit + integration + typecheck + lint + Vitest   (stop the API first)
```

Sample sign-ins (development seed only): `admin@test.local` / `AdminPass#2026`, `customer@test.local` / `CustomerPass#2026`,
`publisher@test.local` / `PublisherPass#2026`. Admins sign in at `/admin/login`.
There are only two account types, Customer and Admin: the `publisher@` accounts are customers that were approved as sellers (they own a seller profile).
A customer applies from their account (`/account/become-seller`) with their documents and an admin approves under `/admin/applications`.

## Legacy project

The old ASP.NET MVC 5 sources were removed from the working tree; they are in git at the tag `legacy-before-removal`
(`git worktree add ../legacy-checkout legacy-before-removal` restores them next to this folder). Details: `docs/REBUILD-REPORT.md` section 14.

## Safety rules built into the tooling

* The seed refuses to run outside Development/Testing or on a database that already has users.
* Tests that need a database create and drop their own `ERecycleHub_Test_*` database.
* The legacy import is a dry run unless `-Execute` is passed, never writes to the source, and refuses a non-empty target.
* E-mail goes to a local folder unless a provider is explicitly configured; no secrets are stored in the repository.
