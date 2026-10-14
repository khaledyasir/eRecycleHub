# eRecycleHub web — conventions

Next.js 16 (App Router, Turbopack, **Cache Components off**), React 19, TypeScript strict, Tailwind CSS 4, next-intl 4.
Read `AGENTS.md`: this Next.js version differs from older docs; its own docs are in `node_modules/next/dist/docs/`.
`middleware.ts` is called **`src/proxy.ts`** in Next 16.

## Architecture

- The browser only talks to this app. `/api/*` is rewritten to the ASP.NET Core API (`API_INTERNAL_URL`, default `http://localhost:5080`),
  so the auth cookie and antiforgery cookie are same-origin.
- **Server components** fetch with `serverApi()` + `load()` from `@/lib/api/server` (forwards the visitor's cookies, language, IP).
  `load()` renders 404 on 404 and throws otherwise. `loadOptional()` returns `null` for 404/204. `getSession()` gives the signed-in user.
- **Client mutations** use `call(() => api.POST("/api/...", { params, body }))` from `@/lib/api/client` (typed by the OpenAPI schema;
  adds `X-CSRF-TOKEN`, retries once on `csrf.invalid`). Multipart endpoints use `sendForm(method, path, data, files)`:
  JSON fields go in the `data` part, files as named parts (`File[]` → repeated parts).
- After a mutation that changes server-rendered data, call `router.refresh()` (from `@/i18n/navigation`'s `useRouter`).
- Business rules live in the API. Client validation (Zod / simple checks) is for convenience only.
- API types: `import type { Schema } from "@/lib/api/types"` → `Schema<"AuctionCard">`. Regenerate with `npm run api:types`
  after the API changes (the API writes `apps/api/openapi/erecyclehub.json` on build).
- Guards: portal layouts call `requireRole([...])` from `@/lib/guard` (UX only — the API authorises every request).

## Internationalisation

- Locales `en` (default) and `ar` (RTL). Every route lives under `src/app/[locale]/`. Use `Link`, `useRouter`, `redirect`, `usePathname` from
  `@/i18n/navigation`, never `next/link` directly.
- Messages: `src/messages/{en,ar}/<namespace>.json`. Namespaces: `common, errors, public, auction, auth, account, seller, admin, cms`.
  **Both languages must have identical keys.** Arabic uses full ICU plural forms (`zero/one/two/few/many/other`).
- No hard-coded UI text anywhere (including `aria-label`, `placeholder`, `alt`, toast and error text).
- API errors: `useErrorMessage()` maps `ApiError.code` → `errors.json` (money args formatted automatically).
- Bilingual content fields from the API come in pairs (`titleEn`/`titleAr`): show with `pick(locale, en, ar)`; edit forms show both.
- Use logical CSS: `ms-/me-/ps-/pe-/start-/end-`, `text-start`, never `left/right`. Direction-sensitive icons get `className="flip-rtl"`.
- Money: `money(amount, locale)`; dates: `dateTime(iso, locale)` / `date(iso, locale)` (display time zone, deterministic on server and client).
  Datetime inputs: `toLocalInput(iso)` / `fromLocalInput(value)`.
- Files: `fileUrl(id)` → `/api/files/{id}`; render uploaded images with a plain `<img>` (add the eslint disable comment for `no-img-element`).
- HTML from the API (sanitised server-side) is only rendered through `<RichHtml html=… />`.

## Design system

Tokens in `src/app/globals.css`: ink `#1c2321`, ground `#f3f5f2`, surface white, line `#d5dad6`, **patina** `#0e6b57` (brand / primary),
**hivis** `#f2b705` (only for what is live and for the bid action), **rust** `#b4441e` (errors, destructive, ending soon). IBM Plex Sans + Arabic.
Radius: controls `rounded-md`, cards `rounded-lg`, dialogs `rounded-xl`. Use `tabular` on numbers. No decorative gradients, no all-caps eyebrows,
no fake data or charts: every number shown comes from the API.

Components (`src/components/ui`): `Button` (variants primary/bid/secondary/ghost/danger/link, `loading`, `asChild`), `form.tsx`
(`Input, Textarea, Select, Label, Field` (render-prop gives id + aria props), `Checkbox, FileInput`), `display.tsx` (`Badge, Skeleton,
EmptyState, Panel, PageHeader, Stat, Alert`), `overlay.tsx` (`Dialog, ConfirmDialog, Menu, MenuItem`), `controls.tsx` (`Switch, Tabs, Table`),
`pagination.tsx` (server `Pagination` with query preservation), `rich-text.tsx` (`RichTextEditor`).
Portal pages: `PortalShell` + nav groups (`src/components/portal`). Every list has loading/empty/error states; destructive and money actions use
`ConfirmDialog`; mutations show a `sonner` toast with the action's own name ("Approve" → "Approved").

## Accessibility

Semantic landmarks/headings, a label for every control (`Field`), visible focus (global), keyboard-operable dialogs/menus (Radix), `aria-live` for
changing prices/results, tables scroll inside their container on phones, respect `prefers-reduced-motion`.

## Checks

`npm run typecheck`, `npm run lint`, `npm test` (Vitest), `npm run test:e2e` (Playwright; needs API + web running against the dev DB).

## Tests

- `npm test` - Vitest (jsdom): formatting, message-catalogue parity (en/ar keys and placeholders), legacy redirects (`src/proxy.ts`),
  `useForm`/validators, the generic CMS manager. Put new tests next to the code as `*.test.ts(x)`.
- `npm run test:e2e` - Playwright (`e2e/`), against a running stack on a seeded **development** database (see `../../scripts/`).
  Use accounts from `e2e/helpers.ts`; tests that change data create their own records so they can be re-run.
- Legacy MVC URL redirects live in `src/proxy.ts` (not `next.config.ts`): they must be case-sensitive for three paths and the config matcher is not.
- Before pushing: `npm run typecheck && npm run lint && npm test`.
