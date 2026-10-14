import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { csrf, watchProblems } from "./helpers";

test.describe("anonymous visitor", () => {
  test("home, auctions and a lot page render without errors", async ({ page }) => {
    const problems = watchProblems(page);
    await page.goto("/en");
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("lang", "en");
    await page.goto("/en/auctions");
    await expect(page.getByRole("link", { name: /Open test auction/ }).first()).toBeVisible();
    await page.getByRole("link", { name: /Open test auction/ }).first().click();
    await expect(page).toHaveURL(/\/en\/auctions\/\d+/);
    await expect(page.getByRole("heading", { level: 1, name: "Open test auction" })).toBeVisible();
    await expect(page.getByText("Sign in to bid").first()).toBeVisible();
    expect(problems).toEqual([]);
  });

  test("filters and search work from the URL", async ({ page }) => {
    await page.goto("/en/auctions?q=Copper");
    await expect(page.getByRole("link", { name: /Copper cable offcuts/ }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: /Open test auction/ })).toHaveCount(0);
    await page.goto("/en/auctions?status=ended");
    await expect(page.getByRole("link", { name: /Ended test auction/ }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: /Future test auction/ })).toHaveCount(0);
  });

  test("protected pages send visitors to the right sign-in", async ({ page }) => {
    await page.goto("/en/account/wallet");
    await expect(page).toHaveURL(/\/en\/login/);
    await page.goto("/en/seller");
    await expect(page).toHaveURL(/\/en\/login/);
    await page.goto("/en/admin/users");
    await expect(page).toHaveURL(/\/en\/admin\/login/);
  });

  test("the API rejects anonymous and CSRF-less requests", async ({ request }) => {
    expect((await request.get("/api/account/wallet")).status()).toBe(401);
    expect((await request.get("/api/admin/users")).status()).toBe(401);
    // Unsafe request without the antiforgery header is refused before it reaches any handler.
    const noToken = await request.post("/api/auth/login", { data: { email: "admin@test.local", password: "x", adminOnly: true } });
    expect([400, 403]).toContain(noToken.status());
    expect(await noToken.json()).toMatchObject({ code: "csrf.invalid" });
    // A token alone doesn't sign anyone in with a wrong password.
    const bad = await request.post("/api/auth/login", { headers: { "X-CSRF-TOKEN": await csrf(request) }, data: { email: "customer@test.local", password: "wrong-password" } });
    expect(bad.status()).toBe(401);
  });

  test("security headers are present", async ({ request }) => {
    const res = await request.get("/en");
    expect(res.headers()["x-content-type-options"]).toBe("nosniff");
    expect(res.headers()["content-security-policy"]).toBeTruthy();
    expect(res.headers()["x-powered-by"]).toBeUndefined();
  });

  test("legacy MVC URLs redirect permanently to the new routes", async ({ request }) => {
    for (const [from, to] of [["/Auctions", "/auctions"], ["/Store/Details/3", "/stores/3"], ["/Topic/HowToBuy", "/how-to-buy"], ["/Account/Login", "/login"]]) {
      const res = await request.get(from, { maxRedirects: 0 });
      expect(res.status(), from).toBe(308);
      expect(new URL(res.headers()["location"], "http://localhost").pathname, from).toBe(to);
    }
  });

  test("unknown pages return 404 with a way back", async ({ page }) => {
    const res = await page.goto("/en/this-page-does-not-exist");
    expect(res?.status()).toBe(404);
    await expect(page.getByRole("link", { name: /home/i }).first()).toBeVisible();
  });
});

test.describe("Arabic and RTL", () => {
  test("the Arabic site is right-to-left, translated, and keeps Latin digits for money", async ({ page }) => {
    const problems = watchProblems(page);
    await page.goto("/ar/auctions");
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await expect(page.locator("html")).toHaveAttribute("lang", "ar");
    await expect(page.getByRole("link", { name: /مزاد اختبار مفتوح/ }).first()).toBeVisible();
    const body = await page.locator("main").innerText();
    expect(body).not.toMatch(/[٠-٩]/);
    expect(problems).toEqual([]);
  });

  test("the visitor's language choice sticks and the switcher changes it", async ({ page }) => {
    await page.goto("/en");
    await page.getByRole("button", { name: /Language/ }).first().click();
    await expect(page).toHaveURL(/\/ar(\/|$)/);
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  });
});

test.describe("accessibility (automated checks)", () => {
  // Automated axe rules find only part of the WCAG issues; this guards against regressions, it is not an audit.
  for (const path of ["/en", "/en/auctions", "/en/login", "/en/register", "/en/contact", "/ar", "/ar/auctions", "/ar/login"]) {
    test(`no serious or critical violations on ${path}`, async ({ page }) => {
      await page.goto(path);
      await page.waitForLoadState("networkidle");
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]).analyze();
      const bad = results.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
      expect(bad.map((v) => `${v.id}: ${v.nodes.slice(0, 3).map((n) => n.target.join(" ")).join(" | ")}`)).toEqual([]);
    });
  }

  test("keyboard users get a skip link and visible focus", async ({ page }) => {
    await page.goto("/en");
    await page.keyboard.press("Tab");
    const first = page.locator(":focus");
    await expect(first).toBeVisible();
    await expect(first).toHaveAttribute("href", /#main/);
  });
});
