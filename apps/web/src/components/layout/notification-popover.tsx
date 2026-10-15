"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Popover as P } from "radix-ui";
import { useState } from "react";
import { Link } from "@/i18n/navigation";
import { api, call } from "@/lib/api/client";
import type { Schema } from "@/lib/api/types";
import { cn, money, pick, relativeTime } from "@/lib/format";

/** Bell with an unread badge; opens a pop-up with the latest notifications instead of navigating away. */
export function NotificationBell() {
  const t = useTranslations("common.user");
  const tn = useTranslations("account.notifications");
  const locale = useLocale();
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const { data } = useQuery({
    queryKey: ["unread-count"],
    queryFn: () => call(() => api.GET("/api/account/notifications/unread-count")),
    refetchInterval: 60_000,
  });
  const list = useQuery({
    queryKey: ["notifications-latest"],
    queryFn: () => call(() => api.GET("/api/account/notifications", { params: { query: { page: 1 } } })),
    enabled: open,
  });
  const count = data?.count ?? 0;
  const items = (list.data?.items ?? []).slice(0, 6);

  const text = (n: Schema<"NotificationDto">) => {
    const args = { amount: n.amount !== null && n.amount !== undefined ? money(n.amount, locale) : "", title: pick(locale, n.auctionTitleEn, n.auctionTitleAr) };
    switch (n.kind) {
      case "auction.new_bid": return tn("kind.newBid", args);
      case "auction.outbid": return tn("kind.outbid", args);
      case "wallet.topup_approved": return tn("kind.topupApproved", args);
      case "wallet.topup_rejected": return tn("kind.topupRejected", args);
      case "auction.won": return tn("kind.won", args);
      default: return n.title || tn("kind.generic");
    }
  };

  const markAll = async () => {
    await call(() => api.POST("/api/account/notifications/read-all"));
    await Promise.all([qc.invalidateQueries({ queryKey: ["unread-count"] }), qc.invalidateQueries({ queryKey: ["notifications-latest"] })]);
  };

  return (
    <P.Root open={open} onOpenChange={setOpen}>
      <P.Trigger className="relative inline-flex h-11 w-11 items-center justify-center rounded-md hover:bg-sunken" aria-label={`${t("notifications")} — ${t("unread", { count })}`}>
        <Bell className="h-5 w-5" aria-hidden />
        {count > 0 && (
          <span className="tabular absolute end-1 top-1 inline-flex h-4 min-w-4 items-center justify-center rounded-full bg-pink px-1 text-[10px] font-bold text-white">{count > 99 ? "99+" : count}</span>
        )}
      </P.Trigger>
      <P.Portal>
        <P.Content align="end" sideOffset={8} className="z-50 w-[min(22rem,92vw)] overflow-hidden rounded-xl border border-line bg-surface shadow-pop focus:outline-none">
          <div className="flex items-center justify-between gap-2 border-b border-line px-4 py-3">
            <P.Close asChild>
              <Link href="/account/notifications" className="font-extrabold hover:text-patina">
                {t("notifications")}
              </Link>
            </P.Close>
            {count > 0 && (
              <button type="button" onClick={() => void markAll()} className="text-xs font-bold text-patina hover:underline">
                {tn("markAll")}
              </button>
            )}
          </div>
          <div className="max-h-96 overflow-y-auto">
            {list.isPending ? (
              <p className="px-4 py-6 text-center text-sm text-muted">…</p>
            ) : items.length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-muted">{tn("empty")}</p>
            ) : (
              <ul className="divide-y divide-line">
                {items.map((n) => (
                  <li key={n.id} className={cn("px-4 py-3", !n.isRead && "bg-patina-50/70")}>
                    <p className={cn("text-sm", !n.isRead && "font-bold")}>{text(n)}</p>
                    <p className="mt-0.5 text-xs text-muted">{relativeTime(n.createdAtUtc, locale, list.dataUpdatedAt)}</p>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <P.Close asChild>
            <Link href="/account/notifications" className="block border-t border-line px-4 py-3 text-center text-sm font-bold text-patina hover:bg-sunken">
              {t("viewAll")}
            </Link>
          </P.Close>
        </P.Content>
      </P.Portal>
    </P.Root>
  );
}
