import type { Metadata } from "next";
import { Bell } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { MarkAllReadButton, NotificationActions } from "@/components/account/account-forms";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import type { Schema } from "@/lib/api/types";
import { cn, dateTime, money, nowMs, pick, relativeTime } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.notifications"))("title") };
}

export default async function Notifications({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const unread = sp.unread === "true";
  const [t, locale, client] = await Promise.all([getTranslations("account.notifications"), getLocale(), serverApi()]);
  const r = await load(client.GET("/api/account/notifications", { params: { query: { unread, page: Math.max(1, Number(sp.page) || 1) } } }));
  const now = nowMs();
  const text = (n: Schema<"NotificationDto">) => {
    const args = { amount: n.amount !== null && n.amount !== undefined ? money(n.amount, locale) : "", title: pick(locale, n.auctionTitleEn, n.auctionTitleAr) };
    switch (n.kind) {
      case "auction.new_bid": return t("kind.newBid", args);
      case "auction.outbid": return t("kind.outbid", args);
      case "wallet.topup_approved": return t("kind.topupApproved", args);
      case "wallet.topup_rejected": return t("kind.topupRejected", args);
      case "auction.won": return t("kind.won", args);
      case "seller.approved": return t("kind.sellerApproved", args);
      case "seller.rejected": return t("kind.sellerRejected", args);
      default: return n.title || t("kind.generic");
    }
  };
  return (
    <>
      <PageHeader title={t("title")} actions={r.items.some((n) => !n.isRead) ? <MarkAllReadButton /> : undefined} />
      <nav className="mb-5 flex gap-1 border-b border-line" aria-label={t("title")}>
        {[[false, t("all")], [true, t("unread")]].map(([u, label]) => (
          <Link key={String(u)} href={u ? "/account/notifications?unread=true" : "/account/notifications"} aria-current={unread === u ? "page" : undefined} className={cn("-mb-px border-b-2 px-3 py-2.5 text-sm font-medium", unread === u ? "border-patina text-ink" : "border-transparent text-muted hover:text-ink")}>
            {label as string}
          </Link>
        ))}
      </nav>
      {r.items.length === 0 ? (
        <EmptyState icon={<Bell className="h-8 w-8" />} title={unread ? t("emptyUnread") : t("empty")} />
      ) : (
        <ul className="divide-y divide-line overflow-hidden rounded-lg border border-line bg-surface">
          {r.items.map((n) => (
            <li key={n.id} className={cn("flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between", !n.isRead && "bg-patina-50/60")}>
              <div className="min-w-0">
                <p className={cn("text-sm", !n.isRead && "font-semibold")}>
                  {!n.isRead && <span className="me-2 inline-block h-2 w-2 rounded-full bg-patina align-middle" aria-hidden />}
                  {text(n)}
                </p>
                {n.kind === "legacy" && n.body && <p className="mt-0.5 text-sm text-muted">{n.body}</p>}
                <p className="mt-0.5 text-xs text-muted" title={dateTime(n.createdAtUtc, locale)}>{relativeTime(n.createdAtUtc, locale, now)}</p>
              </div>
              <NotificationActions id={n.id} isRead={n.isRead} url={n.url ?? null} />
            </li>
          ))}
        </ul>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/account/notifications" query={sp} />
    </>
  );
}
