import type { Metadata } from "next";
import { Plus, Store } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { FlagSwitch } from "@/components/admin/admin-forms";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { cn, date, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.publishers"))("title") };
}

export default async function Publishers({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const active = sp.active !== "false";
  const [t, tu, tc, locale, client] = await Promise.all([getTranslations("admin.publishers"), getTranslations("admin.users"), getTranslations("admin.common"), getLocale(), serverApi()]);
  const r = await load(client.GET("/api/admin/publishers", { params: { query: { active, q: sp.q, page: Math.max(1, Number(sp.page) || 1) } } }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} actions={<Link href="/admin/publishers/new" className="inline-flex h-10 items-center gap-2 rounded-md bg-patina px-4 text-sm font-medium text-white hover:bg-patina-700"><Plus className="h-4 w-4" aria-hidden /> {t("new")}</Link>} />
      <nav className="mb-4 flex gap-1 border-b border-line" aria-label={t("title")}>
        {[true, false].map((a) => (
          <Link key={String(a)} href={a ? "/admin/publishers" : "/admin/publishers?active=false"} aria-current={active === a ? "page" : undefined} className={cn("-mb-px border-b-2 px-3 py-2.5 text-sm font-medium", active === a ? "border-patina text-ink" : "border-transparent text-muted hover:text-ink")}>
            {a ? tu("active") : tu("archived")}
          </Link>
        ))}
      </nav>
      <form className="mb-4 flex gap-2" role="search">
        {!active && <input type="hidden" name="active" value="false" />}
        <label htmlFor="pq" className="sr-only">{tu("search")}</label>
        <input id="pq" name="q" type="search" defaultValue={sp.q} placeholder={tu("search")} className="h-9 w-full max-w-sm rounded-md border border-line-strong bg-surface px-3 text-sm" />
        <button className="h-9 rounded-md bg-ink px-3 text-sm font-medium text-white">{tc("search")}</button>
      </form>
      {r.items.length === 0 ? <EmptyState icon={<Store className="h-8 w-8" />} title={t("empty")} /> : (
        <Table>
          <thead><tr><th>{tu("name")}</th><th>{tu("email")}</th><th>{tu("phone")}</th><th>{tu("country")}</th><th>{t("auctions")}</th><th>{tu("activeLabel")}</th><th>{tu("joined")}</th></tr></thead>
          <tbody>
            {r.items.map((p) => (
              <tr key={p.id}>
                <td><Link href={`/admin/publishers/${p.id}`} className="font-medium hover:underline">{p.name}</Link></td>
                <td>{p.email}</td>
                <td dir="ltr" className="text-start">{p.phone ?? "—"}</td>
                <td>{pick(locale, p.countryNameEn, p.countryNameAr)}</td>
                <td className="tabular"><Link href={`/admin/auctions?publisherId=${p.id}&view=all`} className="hover:underline">{p.auctionCount}</Link></td>
                <td><FlagSwitch path={`/api/admin/publishers/${p.id}/active`} field="active" value={p.isActive} label={`${tu("activeLabel")}: ${p.name}`} on={tu("activated")} off={tu("deactivated")} /></td>
                <td className="whitespace-nowrap text-muted">{date(p.createdAtUtc, locale)}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/admin/publishers" query={sp} />
    </>
  );
}
