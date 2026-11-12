"use client";

import { ChevronLeft, ChevronRight, Trophy, User } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Alert, Badge, Skeleton } from "@/components/ui/display";
import { cn, dateTime, money } from "@/lib/format";
import { useAuctionBids } from "./live";
import { PriceChart } from "./price-chart";

// live=false everywhere here: the bid panel (always on this page) owns the socket and the fallback poll, and its
// invalidation covers every ["auction-bids", id, page] query, so these lists refresh with it without a second connection.

/** Tab label: "Bid history (12)", following the live count once the bids are loaded. */
export function BidHistoryLabel({ auctionId, initialCount }: { auctionId: number; initialCount: number }) {
  const t = useTranslations("auction.detail");
  const total = useAuctionBids(auctionId, 1, false).data?.total ?? initialCount;
  return (
    <>
      {t("history")} <span className="tabular text-muted">({total})</span>
    </>
  );
}

/** Public bids, newest first, with the price graph on top. Bidders are anonymous numbers. */
export function BidHistory({ auctionId, startPrice }: { auctionId: number; startPrice: number }) {
  const t = useTranslations("auction.detail");
  const tc = useTranslations("common");
  const locale = useLocale();
  const [page, setPage] = useState(1);
  const latest = useAuctionBids(auctionId, 1, false);
  const current = useAuctionBids(auctionId, page, false);
  const data = current.data;

  if (!data) {
    return current.isError ? (
      <Alert tone="danger">
        {tc("error.body")}{" "}
        <button type="button" className="font-semibold underline" onClick={() => void current.refetch()}>
          {tc("retry")}
        </button>
      </Alert>
    ) : (
      <Skeleton className="h-40" />
    );
  }
  if (data.total === 0) return <p className="text-sm text-muted">{t("historyEmpty")}</p>;

  // Bids only go up, so the newest bid overall is the highest.
  const topId = latest.data?.items[0]?.id;
  const totalPages = Math.ceil(data.total / data.pageSize);
  const from = (data.page - 1) * data.pageSize + 1;

  return (
    <div className="space-y-4">
      {/* ponytail: the endpoint serves fixed pages of 20, so the graph is the start price plus the latest 20 bids; fetch more pages (or add a trend endpoint) if the whole curve matters. */}
      <PriceChart startPrice={startPrice} bids={(latest.data?.items ?? []).map((b) => b.amount).reverse()} />
      <ul className="divide-y divide-line overflow-hidden rounded-lg border border-line bg-surface">
        {data.items.map((b) => {
          const highest = b.id === topId;
          return (
            <li key={b.id} className="flex items-center gap-3 px-4 py-3">
              <span className={cn("flex h-10 w-10 shrink-0 items-center justify-center rounded-full", highest ? "bg-patina text-white" : "bg-patina-50 text-patina")}>
                {highest ? <Trophy className="h-5 w-5" aria-hidden /> : <User className="h-5 w-5" aria-hidden />}
              </span>
              <div className="min-w-0 flex-1">
                <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm font-semibold">
                  {b.isMine ? t("historyYou") : t("historyBidder", { number: b.bidderNumber })}
                  {highest && (
                    <Badge tone="warn" className="px-2 py-0.5">
                      {t("historyHighest")}
                    </Badge>
                  )}
                </p>
                <p className="text-xs text-muted">{dateTime(b.createdAtUtc, locale)}</p>
              </div>
              <span className={cn("tabular shrink-0 text-sm font-bold", highest && "text-patina")}>{money(b.amount, locale)}</span>
            </li>
          );
        })}
      </ul>
      {totalPages > 1 && (
        <nav aria-label={tc("pagination.label")} className="flex items-center justify-between gap-3">
          <Button variant="ghost" size="icon" aria-label={tc("pagination.previous")} disabled={page <= 1} onClick={() => setPage(page - 1)}>
            <ChevronLeft className="flip-rtl h-4 w-4" aria-hidden />
          </Button>
          <span className="tabular text-sm text-muted">{tc("pagination.showing", { from, to: from + data.items.length - 1, total: data.total })}</span>
          <Button variant="ghost" size="icon" aria-label={tc("pagination.next")} disabled={page >= totalPages} onClick={() => setPage(page + 1)}>
            <ChevronRight className="flip-rtl h-4 w-4" aria-hidden />
          </Button>
        </nav>
      )}
      <p className="text-xs text-muted">{t("historyPrivacy")}</p>
    </div>
  );
}
