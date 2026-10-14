// @vitest-environment node
import { NextRequest } from "next/server";
import { describe, expect, it } from "vitest";
import proxy from "./proxy";

const go = (path: string) => proxy(new NextRequest(new URL(path, "http://localhost:3000")));
const target = (path: string) => {
  const res = go(path);
  return res.status === 308 ? new URL(res.headers.get("location")!).pathname + new URL(res.headers.get("location")!).search : null;
};

describe("legacy URL redirects", () => {
  it.each([
    ["/Auctions", "/auctions"],
    ["/Auctions/Index", "/auctions"],
    ["/auctions/details/42", "/auctions/42"],
    ["/Auctions/Details/42", "/auctions/42"],
    ["/Store/Details/7", "/stores/7"],
    ["/Suppliers/List", "/stores"],
    ["/Clients", "/clients"],
    ["/Topic/HowToBuy", "/how-to-buy"],
    ["/Topic/Condition", "/terms"],
    ["/Topic/PrivacyPolicy", "/privacy"],
    ["/Account/Login", "/login"],
    ["/Account/PasswordRecovery", "/forgot-password"],
    ["/Account/MyAuction", "/account/auctions"],
    ["/Account/Transactions", "/account/wallet"],
    ["/Dashboard/CMSLogin", "/admin/login"],
    ["/Dashboard/Users/Index", "/admin"],
    ["/Stores/Auction/Index", "/seller"],
  ])("%s → %s (308)", (from, to) => {
    expect(target(from)).toBe(to);
  });

  it("keeps the token query only for the password-reset link", () => {
    expect(target("/Account/ResetPassword?token=abc&email=x")).toBe("/reset-password?token=abc&email=x");
    expect(target("/Auctions/Index?page=3")).toBe("/auctions");
  });

  it("does not capture the new lowercase routes (no redirect loops)", () => {
    for (const p of ["/auctions", "/clients", "/stores", "/stores/5", "/en/auctions", "/ar/stores", "/seller"]) {
      expect(go(p).status, p).not.toBe(308);
    }
  });
});
