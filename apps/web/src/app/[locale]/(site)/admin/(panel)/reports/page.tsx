import type { Metadata } from "next";
import { Download } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { PhaseBadge } from "@/components/auction/auction-card";
import { EmptyState, PageHeader, Stat } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { date, fromLocalInput, money, number, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.reports"))("title") };
}

export default async function Reports({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, locale, client] = await Promise.all([getTranslations("admin.reports"), getLocale(), serverApi()]);
  const query = {
    from: sp.from ? fromLocalInput(`${sp.from}T00:00`) : undefined,
    to: sp.to ? fromLocalInput(`${sp.to}T23:59`) : undefined,
    countryId: sp.countryId ? Number(sp.countryId) : undefined,
    page: Math.max(1, Number(sp.page) || 1),
  };
  const [r, countries] = await Promise.all([load(client.GET("/api/admin/reports/auctions", { params: { query } })), load(client.GET("/api/countries"))]);
  const csv = new URLSearchParams({ format: "csv", ...(query.from ? { from: query.from } : {}), ...(query.to ? { to: query.to } : {}), ...(query.countryId ? { countryId: String(query.countryId) } : {}) });
  const input = "h-9 rounded-md border border-line-strong bg-surface px-2 text-sm";
  return (
    <>
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={<a href={`/api/admin/reports/auctions?${csv}`} className="inline-flex h-10 items-center gap-2 rounded-md border border-line-strong bg-surface px-3 text-sm font-medium hover:bg-sunken"><Download className="h-4 w-4" aria-hidden /> {t("export")}</a>}
      />
      <form className="mb-5 flex flex-wrap items-end gap-2">
        <label className="flex flex-col gap-1 text-xs text-muted">{t("from")}<input type="date" name="from" defaultValue={sp.from} className={input} /></label>
        <label className="flex flex-col gap-1 text-xs text-muted">{t("to")}<input type="date" name="to" defaultValue={sp.to} className={input} /></label>
        <label className="flex flex-col gap-1 text-xs text-muted">{t("country")}
          <select name="countryId" defaultValue={sp.countryId ?? ""} className={input}>
            <option value="">{t("allCountries")}</option>
            {countries.map((c) => <option key={c.id} value={c.id}>{pick(locale, c.nameEn, c.nameAr)}</option>)}
          </select>
        </label>
        <button className="h-9 rounded-md bg-ink px-3 text-sm font-medium text-white">{t("apply")}</button>
      </form>
      <div className="mb-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Stat label={t("totalAuctions")} value={number(r.totalAuctions, locale)} />
        <Stat label={t("endedWithBids")} value={number(r.endedWithBids, locale)} />
        <Stat label={t("active")} value={number(r.active, locale)} />
        <Stat label={t("sumFinal")} value={money(r.sumFinalPrices, locale)} />
      </div>
      {r.rows.items.length === 0 ? (
        <EmptyState title={t("empty")} />
      ) : (
        <Table>
          <thead><tr><th>{t("lot")}</th><th>{t("title_")}</th><th>{t("publisher")}</th><th>{t("ends")}</th><th>{t("phase")}</th><th>{t("final")}</th><th>{t("bids")}</th><th>{t("winner")}</th></tr></thead>
          <tbody>
            {r.rows.items.map((x) => (
              <tr key={x.id}>
                <td className="tabular text-muted" dir="ltr">{x.refNumber}</td>
                <td><Link href={`/admin/auctions/${x.id}`} className="font-medium hover:underline">{pick(locale, x.titleEn, x.titleAr)}</Link></td>
                <td className="text-ink-2">{x.publisherName ?? "—"}</td>
                <td className="whitespace-nowrap text-muted">{date(x.endsAtUtc, locale)}</td>
                <td><PhaseBadge phase={x.phase} /></td>
                <td className="tabular">{money(x.currentPrice, locale)}</td>
                <td className="tabular">{x.bidCount}</td>
                <td>{x.winnerName ? <>{x.winnerName}<span className="block text-xs text-muted" dir="ltr">{x.winnerPhone}</span></> : "—"}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={r.rows.page} totalPages={Math.ceil(r.rows.total / r.rows.pageSize)} pathname="/admin/reports" query={sp} />
    </>
  );
}
