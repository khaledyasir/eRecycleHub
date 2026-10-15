import { Gavel, TrendingUp, Users } from "lucide-react";
import type { Metadata } from "next";
import { Star } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { EmptyState, PageHeader, Panel, Stat } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { date, dateTime, money, number, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("seller.dashboard"))("title") };
}

export default async function SellerHome() {
  const [t, locale, client] = await Promise.all([getTranslations("seller.dashboard"), getLocale(), serverApi()]);
  const d = await load(client.GET("/api/seller/dashboard"));
  return (
    <>
      <PageHeader
        title={t("title")}
        actions={<Link href="/seller/auctions/new" className="inline-flex h-10 items-center rounded-md bg-patina px-4 text-sm font-medium text-white hover:bg-patina-700">{t("create")}</Link>}
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Stat icon={Gavel} accent="purple" label={t("live")} value={number(d.live, locale)} tone={d.live > 0 ? "attention" : undefined} />
        <Stat label={t("upcoming")} value={number(d.upcoming, locale)} />
        <Stat label={t("ended")} value={number(d.ended, locale)} />
        <Stat label={t("inactive")} value={number(d.inactive, locale)} hint={`${t("total")}: ${number(d.total, locale)}`} />
        <Stat icon={TrendingUp} accent="orange" label={t("bids")} value={number(d.bids, locale)} />
        <Stat icon={Users} accent="lime" label={t("bidders")} value={number(d.bidders, locale)} />
        <Stat label={t("highest")} value={money(d.highestBid, locale)} />
        <Stat icon={Star} accent="pink" label={t("reviews")} value={number(d.reviews, locale)} hint={d.reviews > 0 ? t("rating", { rating: d.ratingAverage }) : t("noRating")} />
      </div>
      <div className="mt-6 grid gap-4 lg:grid-cols-2">
        <Panel title={t("recentBids")} bodyClassName="p-0">
          {d.recentBids.length === 0 ? (
            <p className="p-4 text-sm text-muted">{t("noBids")}</p>
          ) : (
            <ul className="divide-y divide-line">
              {d.recentBids.map((b) => (
                <li key={b.id} className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 px-4 py-2.5 text-sm">
                  <span className="truncate font-medium">{b.userName}</span>
                  <span className="whitespace-nowrap text-muted">{dateTime(b.createdAtUtc, locale)}</span>
                  <span className="tabular font-semibold">{money(b.amount, locale)}</span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
        <Panel title={t("recentReviews")} bodyClassName="p-0">
          {d.recentReviews.length === 0 ? (
            <EmptyState className="m-4" title={t("noReviews")} />
          ) : (
            <ul className="divide-y divide-line">
              {d.recentReviews.map((r) => (
                <li key={r.id} className="px-4 py-3 text-sm">
                  <div className="flex items-center justify-between gap-2">
                    <Link href={`/auctions/${r.auctionId}`} className="truncate font-medium hover:underline">{pick(locale, r.auctionTitleEn, r.auctionTitleAr)}</Link>
                    <span className="inline-flex items-center gap-0.5" aria-label={`${r.rating}/5`}>
                      {Array.from({ length: r.rating }, (_, i) => <Star key={i} className="h-3.5 w-3.5 fill-hivis text-hivis" aria-hidden />)}
                    </span>
                  </div>
                  {r.comment && <p className="mt-1 text-ink-2">{r.comment}</p>}
                  <p className="mt-1 text-xs text-muted">{r.name} · {date(r.createdAtUtc, locale)}</p>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>
    </>
  );
}
