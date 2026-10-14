import { expect, test, type Browser, type Page } from "@playwright/test";
import { csrf, signIn, stamp, watchProblems, type Who } from "./helpers";

// A 1x1 PNG: valid magic bytes for the upload check.
const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");

/** "YYYY-MM-DDTHH:mm" wall-clock time in the display zone (Asia/Amman), as <input type="datetime-local"> expects. */
function inAmman(offsetHours: number) {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Amman", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23" })
    .formatToParts(new Date(Date.now() + offsetHours * 3_600_000))
    .reduce<Record<string, string>>((a, p) => ((a[p.type] = p.value), a), {});
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
}

async function as(browser: Browser, who: Who): Promise<Page> {
  const ctx = await browser.newContext();
  const page = await ctx.newPage();
  await signIn(page, who);
  return page;
}

/**
 * One whole story on a record the test creates itself: a publisher lists a lot through the form, two customers bid,
 * the wallet is never charged by bidding, and the admin sees the lot. Re-runnable on the same database.
 */
test("publisher lists a lot, customers bid, nobody is charged, admin sees it", async ({ browser }) => {
  test.setTimeout(180_000);
  const title = `E2E lot ${stamp()}`;

  // ---- publisher creates the auction through the real form ----
  const pub = await as(browser, "publisher");
  const pubProblems = watchProblems(pub);
  await pub.goto("/en/seller/auctions/new");
  await pub.getByLabel("Title (English)").fill(title);
  await pub.getByLabel("Starting price").fill("100");
  await pub.getByLabel("Bid increment").fill("10");
  await pub.getByLabel("Insurance amount").fill("50");
  await pub.getByLabel(/^Starts/).fill(inAmman(-1));
  await pub.getByLabel(/^Ends/).fill(inAmman(2));
  await pub.locator('input[type="file"]').first().setInputFiles({ name: "lot.png", mimeType: "image/png", buffer: PNG });
  await pub.getByRole("button", { name: "Create auction" }).click();
  await expect(pub).toHaveURL(/\/seller\/auctions\/\d+/);
  const id = Number(pub.url().match(/auctions\/(\d+)/)![1]);
  expect(pubProblems).toEqual([]);

  // ---- publisher cannot see another publisher's lot, and the new lot is theirs only ----
  const pub2 = await as(browser, "publisher2");
  expect((await pub2.request.get(`/api/seller/auctions/${id}`)).status()).toBe(404);
  await pub2.goto(`/en/seller/auctions/${id}`);
  await expect(pub2.getByRole("heading", { level: 1 })).not.toHaveText(title);

  // ---- customers' wallet before ----
  const c1 = await as(browser, "customer");
  const walletBefore = (await (await c1.request.get("/api/account/wallet")).json()).balance as number;

  // ---- customer 1 bids through the UI ----
  await c1.goto(`/en/auctions/${id}`);
  await expect(c1.getByRole("heading", { level: 1, name: title })).toBeVisible();
  await c1.getByLabel("Your bid").fill("110");
  await c1.getByLabel(/read the lot's terms/).check();
  await c1.getByRole("button", { name: "Place bid" }).click();
  await c1.getByRole("dialog").getByRole("button", { name: /Place bid of/ }).click();
  await expect(c1.getByText("You're the highest bidder.").first()).toBeVisible();

  // ---- customer 2 cannot bid below the new minimum, then outbids ----
  const c2 = await as(browser, "customer2");
  await c2.goto(`/en/auctions/${id}`);
  await c2.getByLabel("Your bid").fill("115");
  await c2.getByRole("button", { name: "Place bid" }).click();
  await expect(c2.getByText(/Enter at least/)).toBeVisible();
  await c2.getByLabel("Your bid").fill("120");
  await c2.getByLabel(/read the lot's terms/).check();
  await c2.getByRole("button", { name: "Place bid" }).click();
  await c2.getByRole("dialog").getByRole("button", { name: /Place bid of/ }).click();
  await expect(c2.getByText("You're the highest bidder.").first()).toBeVisible();

  // ---- the API agrees: price, count and leader ----
  const detail = await (await c1.request.get(`/api/auctions/${id}`)).json();
  expect(detail.card.currentPrice).toBe(120);
  expect(detail.card.bidCount).toBe(2);
  expect(detail.viewer.isLeading).toBe(false); // customer 1 was outbid

  // ---- bidding never charges the wallet: insurance is an eligibility check only ----
  const walletAfter = (await (await c1.request.get("/api/account/wallet")).json()).balance as number;
  expect(walletAfter).toBe(walletBefore);

  // ---- the admin finds the lot and its bids ----
  const admin = await as(browser, "admin");
  await admin.goto(`/en/admin/auctions/${id}`);
  await expect(admin.getByRole("heading", { level: 1 })).toBeVisible();
  const bids = await (await admin.request.get(`/api/admin/auctions/${id}/bids`)).json();
  expect(bids.total).toBe(2);

  for (const p of [pub, pub2, c1, c2, admin]) await p.context().close();
});

test("a customer without enough wallet balance sees why they can't bid", async ({ browser }) => {
  const c1 = await as(browser, "customer");
  await c1.goto("/en/auctions?q=High%20deposit");
  await c1.getByRole("link", { name: /High deposit auction/ }).first().click();
  await expect(c1.getByText(/You need at least/)).toBeVisible();
  await expect(c1.getByRole("button", { name: "Place bid" })).toHaveCount(0);
  // The server enforces it too, whatever the page shows.
  const id = Number(c1.url().match(/auctions\/(\d+)/)![1]);
  const res = await c1.request.post(`/api/auctions/${id}/bids`, { headers: { "X-CSRF-TOKEN": await csrf(c1.request) }, data: { amount: 110, acceptedTerms: true } });
  expect(res.status()).toBeGreaterThanOrEqual(400);
  expect(res.status()).toBeLessThan(500);
  await c1.context().close();
});

test("administrator accounts can't bid", async ({ browser }) => {
  const admin = await as(browser, "admin");
  await admin.goto("/en/auctions?q=Open%20test");
  await admin.getByRole("link", { name: /Open test auction/ }).first().click();
  await expect(admin.getByText("Administrator accounts can't bid.")).toBeVisible();
  await admin.context().close();
});
