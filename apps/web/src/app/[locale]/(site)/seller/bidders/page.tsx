import type { Metadata } from "next";
import { Users } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { date, money } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("seller.bidders"))("title") };
}

export default async function SellerBidders({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, tc, locale, client] = await Promise.all([getTranslations("seller.bidders"), getTranslations("common"), getLocale(), serverApi()]);
  const r = await load(client.GET("/api/seller/bidders", { params: { query: { q: sp.q, page: Math.max(1, Number(sp.page) || 1) } } }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <form className="mb-4 flex gap-2" role="search">
        <label htmlFor="bq" className="sr-only">{t("search")}</label>
        <input id="bq" name="q" type="search" defaultValue={sp.q} placeholder={t("search")} className="h-9 w-full max-w-sm rounded-md border border-line-strong bg-surface px-3 text-sm" />
        <button className="h-9 rounded-md bg-patina px-4 text-sm font-bold text-white hover:bg-patina-700">{tc("search")}</button>
      </form>
      {r.items.length === 0 ? (
        <EmptyState icon={<Users className="h-8 w-8" />} title={t("empty")} />
      ) : (
        <Table>
          <thead><tr><th>{t("name")}</th><th>{t("email")}</th><th>{t("phone")}</th><th>{t("bids")}</th><th>{t("highest")}</th><th>{t("last")}</th></tr></thead>
          <tbody>
            {r.items.map((b) => (
              <tr key={b.userId}>
                <td><Link href={`/seller/bidders/${b.userId}`} className="font-medium hover:underline">{b.fullName}</Link></td>
                <td><a href={`mailto:${b.email}`} className="hover:underline">{b.email}</a></td>
                <td dir="ltr" className="text-start">{b.phone ?? "—"}</td>
                <td className="tabular">{b.bidCount}</td>
                <td className="tabular">{money(b.highestBid, locale)}</td>
                <td className="whitespace-nowrap text-muted">{date(b.lastBidAtUtc, locale)}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/seller/bidders" query={sp} />
    </>
  );
}
