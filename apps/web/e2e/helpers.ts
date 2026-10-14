import { expect, type APIRequestContext, type Page } from "@playwright/test";

/** The well-known accounts created by `seed-dev` (development/test databases only). */
export const accounts = {
  admin: { email: "admin@test.local", password: "AdminPass#2026", adminOnly: true, home: /\/admin$/ },
  customer: { email: "customer@test.local", password: "CustomerPass#2026", adminOnly: false, home: /\/account$/ },
  customer2: { email: "customer2@test.local", password: "CustomerPass#2026", adminOnly: false, home: /\/account$/ },
  publisher: { email: "publisher@test.local", password: "PublisherPass#2026", adminOnly: false, home: /\/seller$/ },
  publisher2: { email: "publisher2@test.local", password: "PublisherPass#2026", adminOnly: false, home: /\/seller$/ },
} as const;
export type Who = keyof typeof accounts;

/** Signs in through the same /api the browser uses, so the page's cookie jar holds the session. */
export async function signIn(page: Page, who: Who) {
  const a = accounts[who];
  const me = await (await page.request.get("/api/auth/me")).json();
  const res = await page.request.post("/api/auth/login", { headers: { "X-CSRF-TOKEN": me.csrfToken }, data: { email: a.email, password: a.password, adminOnly: a.adminOnly } });
  expect(res.ok(), `sign in as ${who}`).toBeTruthy();
}

/** CSRF token for the current session of a request context, for tests that call the API directly. */
export async function csrf(request: APIRequestContext) {
  return (await (await request.get("/api/auth/me")).json()).csrfToken as string;
}

/** Collects browser console errors, failed requests (5xx and unexpected 4xx) and uncaught exceptions. */
export function watchProblems(page: Page, ignore: RegExp[] = []) {
  const problems: string[] = [];
  page.on("console", (m) => m.type() === "error" && problems.push(`console: ${m.text().slice(0, 200)}`));
  page.on("pageerror", (e) => problems.push(`exception: ${e.message}`));
  page.on("response", (r) => {
    const s = r.status();
    if (s >= 500 || (s >= 400 && !ignore.some((re) => re.test(`${s} ${r.url()}`)))) problems.push(`${s} ${r.url()}`);
  });
  return problems;
}

/** Unique suffix so tests that create records can be re-run. */
export const stamp = () => `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
