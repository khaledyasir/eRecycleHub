import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const winners = vi.fn();
const byBidder = vi.fn();

vi.mock("next-intl/server", () => ({ getLocale: async () => "en", getTranslations: async () => (k: string) => k }));
vi.mock("@/i18n/navigation", () => ({ Link: ({ children, href }: { children: ReactNode; href: string }) => <a href={href}>{children}</a> }));
vi.mock("@/lib/api/server", () => ({
  serverApi: async () => ({ GET: (path: string) => (path.endsWith("by-bidder") ? byBidder() : winners()) }),
  load: async (r: Promise<unknown>) => r,
}));
vi.mock("@/components/admin/admin-forms", () => ({ NotifyAllWinnersButton: () => <button>notifyAll</button> }));
vi.mock("@/components/manage/manage-actions", () => ({ NotifyWinnerButton: () => <button>notify</button> }));
vi.mock("@/components/ui/pagination", () => ({ Pagination: () => null }));

import Winners from "./page";

const row = { auctionId: 7, refNumber: "A7", titleEn: "Copper", titleAr: "نحاس", endsAtUtc: "2026-01-01T10:00:00Z", amount: 12.5, userId: 3, userName: "Bob", email: "b@x.c", phone: null, notifiedAtUtc: null };
const page = (items: unknown[]) => ({ items, page: 1, pageSize: 20, total: items.length });
const renderPage = async (sp: Record<string, string> = {}) => render(await Winners({ searchParams: Promise.resolve(sp) }));

describe("admin winners page", () => {
  beforeEach(() => { winners.mockReset(); byBidder.mockReset(); });

  it("shows the empty state when there are no winners", async () => {
    winners.mockResolvedValue(page([]));
    await renderPage();
    expect(screen.getByText("empty")).toBeInTheDocument();
    expect(screen.queryByRole("table")).toBeNull();
  });

  it("renders a winner row with optional fields missing (no phone, no end date, not notified)", async () => {
    winners.mockResolvedValue(page([{ ...row, endsAtUtc: null }]));
    await renderPage();
    expect(screen.getByText("A7")).toBeInTheDocument();
    expect(screen.getByText("Bob")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "notify" })).toBeInTheDocument();
  });

  it("shows the empty state in the by-bidder view too", async () => {
    byBidder.mockResolvedValue([]);
    await renderPage({ view: "bidder" });
    expect(screen.getByText("empty")).toBeInTheDocument();
  });
});
