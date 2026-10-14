# Deployment (no containers)

Two processes behind one TLS-terminating reverse proxy, plus SQL Server. **This guide was written from the code and configuration;
it was not executed on a server.** Verify each step in a staging environment first.

```
Internet ──TLS──> reverse proxy (IIS + ARR, nginx, Caddy…) ──> Next.js (node, :3000) ──> API (Kestrel, :5080, NOT public) ──> SQL Server
```

## 1. Build

```powershell
# API (self-contained folder you copy to the server)
dotnet publish apps/api/src/ERecycleHub.Api/ERecycleHub.Api.csproj -c Release -o publish/api

# Web (build on the server, or build in CI and copy .next + node_modules + package.json + next.config.ts + public)
cd apps/web; npm ci; npm run build
```

Server needs: .NET 10 ASP.NET Core runtime, Node 24 LTS, access to SQL Server.

## 2. Configure (secrets from the host's secret store or machine-level environment variables, never committed)

API — see `apps/api/.env.example` for every setting (`ConnectionStrings__Default`, `App__PublicUrl`, `Storage__Root`, `Email__*`).
Web — see `apps/web/.env.example` (`API_INTERNAL_URL`, and `NEXT_PUBLIC_*`, which are fixed at **build** time).

## 3. Database

Create an empty database and two SQL logins: a privileged one used only for the migration step, and a least-privilege one
(read/write on that database, no DDL) for the running app. Then, with the privileged connection string:

```powershell
$env:ConnectionStrings__Default = "<privileged connection string>"
dotnet publish/api/ERecycleHub.Api.dll migrate
```

The app never migrates or seeds by itself in production, and `seed-dev` refuses to run outside Development/Testing.
To bring over legacy data into the new empty database use `scripts/import-legacy.ps1` (dry run first; see `docs/DATABASE-MAPPING.md`).

## 4. Run the processes

* **API** — a Windows Service (`sc.exe create` or NSSM running `dotnet ERecycleHub.Api.dll`) or IIS with the ASP.NET Core Hosting Bundle.
  Bind it to loopback or a private interface only (`ASPNETCORE_URLS=http://127.0.0.1:5080`). It trusts `X-Forwarded-For` from its caller for rate limiting
  and audit IPs, so it **must not** be reachable from the internet.
* **Web** — `npm run start -- -H 127.0.0.1 -p 3000` as a service (NSSM, pm2 or systemd) with `NODE_ENV=production` and `API_INTERNAL_URL=http://127.0.0.1:5080`.
* **Reverse proxy** — forward everything to the web process, keep `Host` and add `X-Forwarded-For` / `X-Forwarded-Proto`; redirect HTTP to HTTPS.
  Do not publish port 5080.

## 5. Files and backups

`Storage__Root` holds uploaded files (outside any web root; served only through `/api/files/{id}`). Back it up together with the database,
and give the API's service account modify rights on that folder only.

## 6. After deployment

* `GET /api/health` returns `{"status":"ok"}`; sign in at `/admin/login` with an administrator created by your import or by an existing admin.
* Set `Email__Provider=SendGrid` and its key only when ready to send real mail; until then mail is written as files to `Email__PickupDirectory`.
* Rate-limit counters live in each API process's memory; with more than one API instance add a limiter at the proxy.
