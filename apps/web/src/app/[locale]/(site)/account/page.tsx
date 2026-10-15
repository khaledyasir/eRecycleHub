import type { Metadata } from "next";
import { Gavel, TrendingUp, Trophy, Wallet } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { Badge, EmptyState, Stat, type Tone } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { getSession, load, serverApi } from "@/lib/api/server";
import { date, money, number, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.dashboard"))("title") };
}

const tones: Record<string, Tone> = { Leading: "ok", Outbid: "warn", Won: "brand", Lost: "neutral" };

export default async function AccountHome({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, locale, client, user] = await Promise.all([getTranslations("account.dashboard"), getLocale(), serverApi(), getSession()]);
  const page = Math.max(1, Number(sp.page) || 1);
  const [d, bids] = await Promise.all([load(client.GET("/api/account/dashboard")), load(client.GET("/api/account/bids", { params: { query: { page } } }))]);
  return (
    <>
      <section className="soft-hero mb-7 flex flex-col items-center justify-between gap-6 rounded-[1.75rem] border border-patina-100 p-6 sm:p-9 md:flex-row">
        <div>
          <h1 className="text-2xl font-black sm:text-3xl">{t("hello", { name: user?.fullName ?? "" })}</h1>
          <Link href="/auctions" className="mt-5 inline-flex items-center gap-2 rounded-md bg-patina px-5 py-3 text-sm font-bold text-white hover:bg-patina-700">
            {t("browse")}
          </Link>
        </div>
        <div className="grid h-36 w-36 shrink-0 place-items-center rounded-full bg-cyan/20" aria-hidden>
          <div className="grid h-24 w-24 place-items-center rounded-full bg-patina text-white">
            <Gavel className="h-12 w-12" />
          </div>
        </div>
      </section>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Stat label={t("attended")} value={number(d.auctionsAttended, locale)} icon={Gavel} accent="purple" />
        <Stat label={t("leading")} value={number(d.leading, locale)} icon={TrendingUp} accent="orange" />
        <Stat label={t("won")} value={number(d.won, locale)} icon={Trophy} accent="lime" />
        <Stat label={t("wallet")} value={money(d.walletBalance, locale)} icon={Wallet} hint={<Link href="/account/wallet" className="text-patina hover:underline">{t("walletLink")}</Link>} />
      </div>
      {d.unreadNotifications > 0 && (
        <p className="mt-3 text-sm">
          <Link href="/account/notifications?unread=true" className="font-medium text-patina hover:underline">{t("unread")}: {number(d.unreadNotifications, locale)}</Link>
        </p>
      )}
      <h2 className="mb-3 mt-8 text-lg font-semibold">{t("bids")}</h2>
      {bids.items.length === 0 ? (
        <EmptyState icon={<Gavel className="h-8 w-8" />} title={t("bidsEmpty")} action={<Link href="/auctions" className="text-sm font-medium text-patina hover:underline">{t("browse")}</Link>} />
      ) : (
        <Table>
          <thead>
            <tr><th>{t("lot")}</th><th>{t("auction")}</th><th>{t("yourBid")}</th><th>{t("current")}</th><th>{t("placed")}</th><th>{t("status")}</th></tr>
          </thead>
          <tbody>
            {bids.items.map((b) => (
              <tr key={b.id}>
                <td className="tabular text-muted" dir="ltr">{b.refNumber}</td>
                <td><Link href={`/auctions/${b.auctionId}`} className="font-medium hover:underline">{pick(locale, b.titleEn, b.titleAr)}</Link></td>
                <td className="tabular">{money(b.amount, locale)}</td>
                <td className="tabular">{money(b.currentPrice, locale)}</td>
                <td className="text-muted">{date(b.createdAtUtc, locale)}</td>
                <td><Badge tone={tones[b.status]}>{t(`statusLabel.${b.status}`)}</Badge></td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={bids.page} totalPages={Math.ceil(bids.total / bids.pageSize)} pathname="/account" query={sp} />
    </>
  );
}
