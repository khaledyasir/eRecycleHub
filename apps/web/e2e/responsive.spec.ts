import { expect, test } from "@playwright/test";
import { signIn, watchProblems, type Who } from "./helpers";

// Phone-sized viewport (see the "phone" project): nothing may be wider than the screen, and no errors in the console.
const pages: [Who | null, string][] = [
  [null, "/en"], [null, "/en/auctions"], [null, "/ar/auctions"], [null, "/en/auctions?status=ended"], [null, "/en/stores"], [null, "/en/login"], [null, "/en/register"],
  [null, "/en/faq"], [null, "/ar/contact"],
  ["customer", "/en/account"], ["customer", "/en/account/wallet"], ["customer", "/ar/account/wallet"], ["customer", "/en/account/calendar"],
  ["publisher", "/en/seller"], ["publisher", "/en/seller/auctions"], ["publisher", "/ar/seller/auctions/new"],
  ["admin", "/en/admin"], ["admin", "/ar/admin"], ["admin", "/en/admin/transactions"], ["admin", "/en/admin/users"], ["admin", "/en/admin/categories"], ["admin", "/ar/admin/winners"],
];

for (const [who, path] of pages) {
  test(`${who ?? "visitor"} ${path} fits a phone screen`, async ({ page }) => {
    const problems = watchProblems(page);
    if (who) await signIn(page, who);
    await page.goto(path);
    await page.waitForLoadState("networkidle");
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, "horizontal overflow in px").toBeLessThanOrEqual(1);
    await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();
    expect(problems).toEqual([]);
  });
}
