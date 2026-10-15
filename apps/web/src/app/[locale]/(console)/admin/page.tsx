import { CircleDollarSign, Gavel, Store, TrendingUp, Users, Wallet } from "lucide-react";
import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { BidsChart } from "@/components/admin/bar-chart";
import { PhaseBadge } from "@/components/auction/auction-card";
import { PageHeader, Panel, Stat } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { date, money, number, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.dashboard"))("title") };
}

export default async function AdminDashboard() {
  const [t, locale, client] = await Promise.all([getTranslations("admin.dashboard"), getLocale(), serverApi()]);
  const d = await load(client.GET("/api/admin/dashboard"));
  const attention = [
    { href: "/admin/applications", text: t("pendingApplications", { count: d.pendingApplications }), on: d.pendingApplications > 0 },
    { href: "/admin/transactions?status=Pending", text: t("pendingTopUps", { count: d.pendingTopUps, amount: money(d.pendingTopUpAmount, locale) }), on: d.pendingTopUps > 0 },
    { href: "/admin/messages?unread=true", text: t("unreadMessages", { count: d.unreadMessages }), on: d.unreadMessages > 0 },
  ];
  return (
    <>
      <PageHeader title={t("title")} />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Stat icon={Gavel} accent="purple" label={t("live")} value={number(d.live, locale)} tone={d.live > 0 ? "attention" : undefined} hint={`${t("upcoming")}: ${number(d.upcoming, locale)} / ${t("ended")}: ${number(d.ended, locale)}`} />
        <Stat icon={Users} accent="orange" label={t("customers")} value={number(d.customers, locale)} />
        <Stat icon={Store} accent="lime" label={t("publishers")} value={number(d.publishers, locale)} />
        <Stat icon={Wallet} accent="patina" label={t("walletTotal")} value={money(d.walletTotal, locale)} />
      </div>
      <div className="mt-4 grid gap-4 lg:grid-cols-[1fr_1.4fr]">
        <Panel title={t("attention")} bodyClassName="p-0">
          <ul className="divide-y divide-line">
            {attention.map((a) => (
              <li key={a.href} className="flex items-center justify-between gap-3 px-4 py-3 text-sm">
                <span className={a.on ? "font-semibold" : "text-muted"}>{a.text}</span>
                {a.on && <Link href={a.href} className="shrink-0 font-medium text-patina hover:underline">{t("review")}</Link>}
              </li>
            ))}
          </ul>
        </Panel>
        <Panel title={t("chartTitle")}>
          <BidsChart points={d.last30Days} />
        </Panel>
      </div>
      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        <Stat icon={TrendingUp} accent="pink" label={t("bidsMonth")} value={number(d.bidsThisMonth, locale)} hint={t("bidsCompare", { last: d.bidsLastMonth })} />
        <Stat icon={CircleDollarSign} accent="purple" label={t("volumeMonth")} value={money(d.volumeThisMonth, locale)} hint={t("volumeCompare", { amount: money(d.volumeLastMonth, locale) })} />
      </div>
      <div className="mt-4 grid gap-4 xl:grid-cols-3">
        <Panel title={t("recentAuctions")} actions={<Link href="/admin/auctions" className="text-sm text-patina hover:underline">{t("viewAll")}</Link>} bodyClassName="p-0">
          <ul className="divide-y divide-line">
            {d.recentAuctions.map((a) => (
              <li key={a.id} className="flex items-center justify-between gap-2 px-4 py-2.5 text-sm">
                <Link href={`/admin/auctions/${a.id}`} className="min-w-0 truncate font-medium hover:underline">{pick(locale, a.titleEn, a.titleAr)}</Link>
                <PhaseBadge phase={a.phase} />
              </li>
            ))}
          </ul>
        </Panel>
        <Panel title={t("recentCustomers")} actions={<Link href="/admin/users" className="text-sm text-patina hover:underline">{t("viewAll")}</Link>} bodyClassName="p-0">
          <ul className="divide-y divide-line">
            {d.recentCustomers.map((u) => (
              <li key={u.id} className="flex items-center justify-between gap-2 px-4 py-2.5 text-sm">
                <Link href={`/admin/users/${u.id}`} className="min-w-0 truncate font-medium hover:underline">{u.fullName}</Link>
                <span className="shrink-0 text-xs text-muted">{date(u.createdAtUtc, locale)}</span>
              </li>
            ))}
          </ul>
        </Panel>
        <Panel title={t("recentPublishers")} actions={<Link href="/admin/publishers" className="text-sm text-patina hover:underline">{t("viewAll")}</Link>} bodyClassName="p-0">
          <ul className="divide-y divide-line">
            {d.recentPublishers.map((p) => (
              <li key={p.id} className="flex items-center justify-between gap-2 px-4 py-2.5 text-sm">
                <Link href={`/admin/publishers/${p.id}`} className="min-w-0 truncate font-medium hover:underline">{p.name}</Link>
                <span className="tabular shrink-0 text-xs text-muted">{p.auctionCount}</span>
              </li>
            ))}
          </ul>
        </Panel>
      </div>
    </>
  );
}
