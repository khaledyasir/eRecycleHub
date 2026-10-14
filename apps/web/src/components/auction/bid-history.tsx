"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/display";
import { cn, dateTime, money } from "@/lib/format";
import { useAuctionBids } from "./live";

/** Public bid history: amounts and times, bidders as numbers (identities are only shown to the seller and administrators). */
export function BidHistory({ auctionId, live }: { auctionId: number; live: boolean }) {
  const t = useTranslations("auction.detail");
  const tp = useTranslations("common.pagination");
  const tc = useTranslations("common");
  const locale = useLocale();
  const [page, setPage] = useState(1);
  const { data, isLoading, isError, refetch } = useAuctionBids(auctionId, page, live && page === 1);
  if (isLoading) return <Skeleton className="h-40" />;
  if (isError || !data)
    return (
      <Button variant="secondary" onClick={() => refetch()}>
        {tc("retry")}
      </Button>
    );
  if (data.total === 0) return <p className="text-sm text-muted">{t("historyEmpty")}</p>;
  const pages = Math.ceil(data.total / data.pageSize);
  return (
    <div>
      <ol className="divide-y divide-line rounded-lg border border-line bg-surface">
        {data.items.map((b, i) => (
          <li key={b.id} className={cn("flex items-center justify-between gap-4 px-4 py-2.5 text-sm", page === 1 && i === 0 && "bg-hivis-soft")}>
            <span className={cn(b.isMine && "font-semibold text-patina-700")}>{b.isMine ? t("historyYou") : t("historyBidder", { number: b.bidderNumber })}</span>
            <span className="text-muted">{dateTime(b.createdAtUtc, locale)}</span>
            <span className="tabular font-semibold">{money(b.amount, locale)}</span>
          </li>
        ))}
      </ol>
      <div className="mt-3 flex items-center justify-between text-sm text-muted">
        <span>{t("historyPrivacy")}</span>
        {pages > 1 && (
          <span className="flex gap-2">
            <Button size="sm" variant="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              {tp("previous")}
            </Button>
            <Button size="sm" variant="secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>
              {tp("next")}
            </Button>
          </span>
        )}
      </div>
    </div>
  );
}
