import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { NotifyAllWinnersButton } from "@/components/admin/admin-forms";
import { NotifyWinnerButton } from "@/components/manage/manage-actions";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { cn, date, dateTime, fromLocalInput, money, number, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.winners"))("title") };
}

export default async function Winners({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const view = sp.view === "bidder" ? "bidder" : "auction";
  const [t, locale, client] = await Promise.all([getTranslations("admin.winners"), getLocale(), serverApi()]);
  const from = sp.from ? fromLocalInput(`${sp.from}T00:00`) : undefined;
  const to = sp.to ? fromLocalInput(`${sp.to}T23:59`) : undefined;
  const input = "h-9 rounded-md border border-line-strong bg-surface px-2 text-sm";
  const tabs = (
    <nav className="mb-4 flex gap-1 border-b border-line" aria-label={t("title")}>
      {(["auction", "bidder"] as const).map((v) => (
        <Link key={v} href={`/admin/winners?view=${v}`} aria-current={view === v ? "page" : undefined} className={cn("-mb-px border-b-2 px-3 py-2.5 text-sm font-medium", view === v ? "border-patina text-ink" : "border-transparent text-muted hover:text-ink")}>
          {v === "auction" ? t("byAuction") : t("byBidder")}
        </Link>
      ))}
    </nav>
  );
  const filters = (
    <form className="mb-4 flex flex-wrap items-end gap-2">
      <input type="hidden" name="view" value={view} />
      {view === "auction" && <label className="flex flex-col gap-1 text-xs text-muted">{t("search")}<input name="q" type="search" defaultValue={sp.q} className={input} /></label>}
      <label className="flex flex-col gap-1 text-xs text-muted">{t("ended")}<input type="date" name="from" defaultValue={sp.from} className={input} /></label>
      <label className="flex flex-col gap-1 text-xs text-muted">&nbsp;<input type="date" name="to" defaultValue={sp.to} className={input} aria-label={t("ended")} /></label>
      {view === "auction" && (
        <label className="flex flex-col gap-1 text-xs text-muted">{t("notified")}
          <select name="notified" defaultValue={sp.notified ?? ""} className={input}>
            <option value="">{t("notifiedAny")}</option>
            <option value="true">{t("notifiedYes")}</option>
            <option value="false">{t("notifiedNo")}</option>
          </select>
        </label>
      )}
      <button className="h-9 rounded-md bg-ink px-3 text-sm font-medium text-white">{t("filter")}</button>
    </form>
  );

  if (view === "bidder") {
    const groups = await load(client.GET("/api/admin/winners/by-bidder", { params: { query: { from, to } } }));
    return (
      <>
        <PageHeader title={t("title")} description={t("description")} actions={<NotifyAllWinnersButton />} />
        {tabs}{filters}
        {groups.length === 0 ? <EmptyState title={t("empty")} /> : (
          <Table>
            <thead><tr><th>{t("winner")}</th><th>{t("contact")}</th><th>{t("won")}</th><th>{t("total")}</th><th>{t("first")}</th><th>{t("last")}</th></tr></thead>
            <tbody>
              {groups.map((g) => (
                <tr key={g.userId}>
                  <td><Link href={`/admin/users/${g.userId}`} className="font-medium hover:underline">{g.userName}</Link></td>
                  <td className="text-sm"><a href={`mailto:${g.email}`} className="block hover:underline">{g.email}</a><span className="block text-muted" dir="ltr">{g.phone}</span></td>
                  <td className="tabular">{number(g.auctionsWon, locale)}</td>
                  <td className="tabular font-semibold">{money(g.totalAmount, locale)}</td>
                  <td className="text-muted">{date(g.firstEndUtc, locale)}</td>
                  <td className="text-muted">{date(g.lastEndUtc, locale)}</td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </>
    );
  }

  const r = await load(client.GET("/api/admin/winners", { params: { query: { from, to, q: sp.q, notified: sp.notified === "true" ? true : sp.notified === "false" ? false : undefined, page: Math.max(1, Number(sp.page) || 1) } } }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} actions={<NotifyAllWinnersButton />} />
      {tabs}{filters}
      {r.items.length === 0 ? <EmptyState title={t("empty")} /> : (
        <Table>
          <thead><tr><th>{t("lot")}</th><th>{t("auction")}</th><th>{t("ended")}</th><th>{t("amount")}</th><th>{t("winner")}</th><th>{t("notifiedAt")}</th><th><span className="sr-only">{t("notify")}</span></th></tr></thead>
          <tbody>
            {r.items.map((w) => (
              <tr key={w.auctionId}>
                <td className="tabular text-muted" dir="ltr">{w.refNumber}</td>
                <td><Link href={`/admin/auctions/${w.auctionId}`} className="font-medium hover:underline">{pick(locale, w.titleEn, w.titleAr)}</Link></td>
                <td className="whitespace-nowrap text-muted">{date(w.endsAtUtc, locale)}</td>
                <td className="tabular font-semibold">{money(w.amount, locale)}</td>
                <td><Link href={`/admin/users/${w.userId}`} className="hover:underline">{w.userName}</Link><span className="block text-xs text-muted">{w.email}</span><span className="block text-xs text-muted" dir="ltr">{w.phone}</span></td>
                <td className="whitespace-nowrap text-muted">{w.notifiedAtUtc ? dateTime(w.notifiedAtUtc, locale) : "—"}</td>
                <td>{!w.notifiedAtUtc && <NotifyWinnerButton auctionId={w.auctionId} />}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/admin/winners" query={sp} />
    </>
  );
}
