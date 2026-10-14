# eRecycleHub web

Next.js 16 (App Router) front end for the eRecycleHub API. Conventions and architecture notes: [CONTRIBUTING.md](CONTRIBUTING.md).
Project overview and setup: [../../README.md](../../README.md).

```bash
cp .env.example .env.local     # API_INTERNAL_URL, NEXT_PUBLIC_SITE_URL, ...
npm ci
npm run dev                    # http://localhost:3000 (needs the API on :5080)

npm run typecheck && npm run lint && npm test      # static checks + Vitest
npm run build && npm run start                     # production build
npx playwright test                                # end to end; needs API + web running on a seeded dev database
npm run api:types                                  # regenerate src/lib/api/schema.d.ts from ../api/openapi/erecyclehub.json
```
