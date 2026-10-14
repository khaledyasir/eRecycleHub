import { expect, test } from "@playwright/test";
import { accounts, csrf, signIn, stamp, watchProblems, type Who } from "./helpers";

test.describe("signing in", () => {
  test("a wrong password stays on the page with a clear message; the right one goes to the role's home", async ({ page }) => {
    await page.goto("/en/login");
    await page.getByLabel("Email").fill(accounts.customer.email);
    await page.getByLabel(/^Password/).fill("not-the-password");
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page.getByRole("alert")).toBeVisible();
    await expect(page).toHaveURL(/\/en\/login/);
    await page.getByLabel(/^Password/).fill(accounts.customer.password);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page).toHaveURL(/\/en\/account$/);
  });

  test("publisher and admin each have their own door", async ({ page }) => {
    await page.goto("/en/login");
    await page.getByLabel("Email").fill(accounts.publisher.email);
    await page.getByLabel(/^Password/).fill(accounts.publisher.password);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page).toHaveURL(/\/en\/seller$/);
    await page.context().clearCookies();
    // An administrator's credentials don't work on the public form's admin-only counterpart for other roles.
    await page.goto("/en/admin/login");
    await page.getByLabel("Email").fill(accounts.customer.email);
    await page.getByLabel(/^Password/).fill(accounts.customer.password);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page).toHaveURL(/\/en\/admin\/login/);
    await page.getByLabel("Email").fill(accounts.admin.email);
    await page.getByLabel(/^Password/).fill(accounts.admin.password);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page).toHaveURL(/\/en\/admin$/);
  });

  test("signing out ends the session", async ({ page }) => {
    await signIn(page, "customer");
    await page.goto("/en/account");
    await page.getByRole("button", { name: /^My account$/i }).first().click();
    await page.getByRole("menuitem", { name: /sign out/i }).click();
    await expect.poll(async () => (await page.request.get("/api/account/wallet")).status()).toBe(401);
  });
});

test.describe("role boundaries", () => {
  const cases: [Who, string, RegExp][] = [
    ["customer", "/en/seller", /\/en\/account$/],
    ["customer", "/en/admin", /\/en\/account$/],
        ["publisher", "/en/admin/users", /\/en\/seller$/],
    ["admin", "/en/account", /\/en\/admin$/],
    ["admin", "/en/seller", /\/en\/admin$/],
  ];
  for (const [who, path, lands] of cases) {
    test(`${who} opening ${path} is sent to their own area`, async ({ page }) => {
      await signIn(page, who);
      await page.goto(path);
      await expect(page).toHaveURL(lands);
    });
  }

  test("a publisher can also use the buyer area (they may bid on other sellers' lots), but never the admin area", async ({ page }) => {
    await signIn(page, "publisher");
    await page.goto("/en/account");
    await expect(page).toHaveURL(/\/en\/account$/);
    await page.goto("/en/admin");
    await expect(page).toHaveURL(/\/en\/seller$/);
  });

  test("the API enforces roles regardless of the pages", async ({ page }) => {
    await signIn(page, "customer");
    expect((await page.request.get("/api/admin/users")).status()).toBe(403);
    expect((await page.request.get("/api/seller/auctions")).status()).toBe(403);
    await page.context().clearCookies();
    await signIn(page, "publisher");
    expect((await page.request.get("/api/admin/users")).status()).toBe(403);
    expect((await page.request.get("/api/seller/auctions")).status()).toBe(200); // their own area works
  });
});

test.describe("wallet: request, approval, idempotency", () => {
  test("a top-up changes the balance only when approved, and only once", async ({ browser }) => {
    test.setTimeout(120_000);
    const cents = String(10 + Math.floor(Math.random() * 89)).padStart(2, "0");
    const amount = `${20 + Math.floor(Math.random() * 60)}.${cents}`;

    const cust = await (await browser.newContext()).newPage();
    const custProblems = watchProblems(cust);
    await signIn(cust, "customer");
    const balance = async () => (await (await cust.request.get("/api/account/wallet")).json()).balance as number;
    const before = await balance();

    // Request through the form.
    await cust.goto("/en/account/wallet");
    await cust.getByLabel("Amount").fill(amount);
    await cust.getByRole("button", { name: "Submit request" }).click();
    await expect(cust.getByText("Request submitted — waiting for approval.")).toBeVisible();
    expect(await balance()).toBe(before); // pending: nothing credited yet
    const txs = await (await cust.request.get("/api/account/wallet/transactions")).json();
    const tx = txs.items.find((t: { amount: number; status: string }) => t.amount === Number(amount) && t.status === "Pending");
    expect(tx, "the pending request is listed").toBeTruthy();

    // Admin approves through the UI.
    const admin = await (await browser.newContext()).newPage();
    await signIn(admin, "admin");
    await admin.goto(`/en/admin/transactions?status=Pending&q=${encodeURIComponent("Test Customer")}`);
    const row = admin.getByRole("row").filter({ hasText: amount });
    await row.getByRole("button", { name: "Approve" }).click();
    await admin.getByRole("dialog").getByRole("button", { name: "Approve" }).click();
    await expect(admin.getByText("Approved — wallet credited.")).toBeVisible();
    expect(await balance()).toBeCloseTo(before + Number(amount), 2);

    // A second approval (double click, second admin, replayed request) must not credit again.
    const again = await admin.request.post(`/api/admin/transactions/${tx.id}/approve`, { headers: { "X-CSRF-TOKEN": await csrf(admin.request) } });
    expect(again.status()).toBeGreaterThanOrEqual(400);
    expect(await balance()).toBeCloseTo(before + Number(amount), 2);

    // The ledger records exactly one credit for it.
    const ledger = await (await cust.request.get("/api/account/wallet/ledger")).json();
    expect(ledger.items.filter((l: { walletTransactionId?: number }) => l.walletTransactionId === tx.id)).toHaveLength(1);
    expect(custProblems.filter((p) => !/^4\d\d /.test(p))).toEqual([]);
    await cust.context().close();
    await admin.context().close();
  });

  test("submitting the same top-up twice (same idempotency key) creates one request", async ({ page }) => {
    await signIn(page, "customer2");
    const key = crypto.randomUUID();
    const post = async () =>
      page.request.post("/api/account/wallet/top-ups", {
        headers: { "X-CSRF-TOKEN": await csrf(page.request) },
        multipart: { data: JSON.stringify({ amount: 7.77, idempotencyKey: key }) },
      });
    const first = await post();
    const second = await post();
    expect(first.ok() && second.ok()).toBeTruthy();
    expect((await first.json()).id).toBe((await second.json()).id);
  });
});

test.describe("admin content management", () => {
  test("categories can be created, edited and deleted, and the public site follows", async ({ page }) => {
    const name = `E2E category ${stamp()}`;
    await signIn(page, "admin");
    const problems = watchProblems(page);
    await page.goto("/en/admin/categories");
    await page.getByRole("button", { name: "Add a category" }).click();
    let dlg = page.getByRole("dialog");
    await dlg.getByRole("button", { name: "Create" }).click();
    await expect(dlg.getByRole("alert")).toHaveCount(3); // country and both titles are required
    await dlg.getByLabel("Country").selectOption({ index: 0 });
    await dlg.getByLabel("Title (English)").fill(name);
    await dlg.getByLabel("Title (Arabic)").fill("فئة اختبار");
    await dlg.getByRole("button", { name: "Create" }).click();
    await expect(page.getByRole("cell", { name, exact: true })).toBeVisible();
    const publicCategories = await (await page.request.get("/api/categories?countryId=1")).json();
    expect(JSON.stringify(publicCategories)).toContain(name); // the public site's category list follows immediately

    await page.goto("/en/admin/categories");
    await page.getByRole("button", { name: `Edit: ${name}`, exact: true }).click();
    dlg = page.getByRole("dialog");
    await dlg.getByLabel("Title (English)").fill(`${name} v2`);
    await dlg.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("cell", { name: `${name} v2`, exact: true })).toBeVisible();

    await page.getByRole("button", { name: `Delete: ${name} v2`, exact: true }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Delete" }).click();
    await expect(page.getByRole("cell", { name: `${name} v2`, exact: true })).toHaveCount(0);
    expect(problems.filter((p) => !/^4\d\d /.test(p))).toEqual([]);
  });

  test("a text override is saved, served to the site, and can be reset", async ({ page }) => {
    await signIn(page, "admin");
    const text = `Sign in (${stamp()})`;
    await page.goto("/en/admin/translations?q=auth.login.title");
    const row = page.getByRole("row").filter({ hasText: "auth.login.title" }).first();
    await row.locator("textarea").fill(text);
    await row.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("Text saved.")).toBeVisible();
    // The site picks overrides up within a minute (cached); the API serves them immediately.
    expect((await (await page.request.get("/api/i18n/en")).json())["auth.login.title"]).toBe(text);
    await row.getByRole("button", { name: "Reset" }).click();
    await expect(page.getByText("Text reset.")).toBeVisible();
    expect((await (await page.request.get("/api/i18n/en")).json())["auth.login.title"]).toBeUndefined();
  });

  test("contact messages show up for the admin and can be marked as read", async ({ browser }) => {
    const visitor = await (await browser.newContext()).newPage();
    const msg = `E2E message ${stamp()}`;
    const res = await visitor.request.post("/api/contact", {
      headers: { "X-CSRF-TOKEN": await csrf(visitor.request) },
      data: { name: "E2E Visitor", email: "e2e@test.local", mobile: "0790000000", message: msg },
    });
    expect(res.ok()).toBeTruthy();
    const admin = await (await browser.newContext()).newPage();
    await signIn(admin, "admin");
    await admin.goto("/en/admin/messages?unread=true");
    const card = admin.getByRole("listitem").filter({ hasText: msg });
    await expect(card).toBeVisible();
    await card.getByRole("button", { name: "Mark as read" }).click();
    await expect(admin.getByText("Marked as read.")).toBeVisible();
    await visitor.context().close();
    await admin.context().close();
  });
});
