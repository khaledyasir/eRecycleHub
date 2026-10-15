import type { Metadata } from "next";
import { Plus, Users } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { FlagSwitch } from "@/components/admin/admin-forms";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { cn, date, money, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.users"))("title") };
}

export default async function Customers({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const active = sp.active !== "false";
  const [t, tc, locale, client] = await Promise.all([getTranslations("admin.users"), getTranslations("admin.common"), getLocale(), serverApi()]);
  const r = await load(client.GET("/api/admin/users", { params: { query: { active, q: sp.q, page: Math.max(1, Number(sp.page) || 1) } } }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} actions={<Link href="/admin/users/new" className="inline-flex h-10 items-center gap-2 rounded-md bg-patina px-4 text-sm font-medium text-white hover:bg-patina-700"><Plus className="h-4 w-4" aria-hidden /> {t("new")}</Link>} />
      <nav className="mb-4 flex gap-1 border-b border-line" aria-label={t("title")}>
        {[true, false].map((a) => (
          <Link key={String(a)} href={a ? "/admin/users" : "/admin/users?active=false"} aria-current={active === a ? "page" : undefined} className={cn("-mb-px border-b-2 px-3 py-2.5 text-sm font-medium", active === a ? "border-patina text-ink" : "border-transparent text-muted hover:text-ink")}>
            {a ? t("active") : t("archived")}
          </Link>
        ))}
      </nav>
      <form className="mb-4 flex gap-2" role="search">
        {!active && <input type="hidden" name="active" value="false" />}
        <label htmlFor="uq" className="sr-only">{t("search")}</label>
        <input id="uq" name="q" type="search" defaultValue={sp.q} placeholder={t("search")} className="h-9 w-full max-w-sm rounded-md border border-line-strong bg-surface px-3 text-sm" />
        <button className="h-9 rounded-md bg-patina px-4 text-sm font-bold text-white hover:bg-patina-700">{tc("search")}</button>
      </form>
      {r.items.length === 0 ? <EmptyState icon={<Users className="h-8 w-8" />} title={t("empty")} /> : (
        <Table>
          <thead><tr><th>{t("name")}</th><th>{t("email")}</th><th>{t("phone")}</th><th>{t("country")}</th><th>{t("wallet")}</th><th>{t("bids")}</th><th>{t("featured")}</th><th>{t("activeLabel")}</th><th>{t("joined")}</th></tr></thead>
          <tbody>
            {r.items.map((u) => (
              <tr key={u.id}>
                <td><Link href={`/admin/users/${u.id}`} className="font-medium hover:underline">{u.fullName}</Link></td>
                <td>{u.email}</td>
                <td dir="ltr" className="text-start">{u.phone ?? "—"}</td>
                <td>{pick(locale, u.countryNameEn, u.countryNameAr)}</td>
                <td className="tabular">{money(u.walletBalance, locale)}</td>
                <td className="tabular">{u.bidCount}</td>
                <td><FlagSwitch path={`/api/admin/users/${u.id}/featured`} field="featured" value={u.isFeaturedClient} label={`${t("featured")}: ${u.fullName}`} on={t("featuredOn")} off={t("featuredOff")} /></td>
                <td><FlagSwitch path={`/api/admin/users/${u.id}/active`} field="active" value={u.isActive} label={`${t("activeLabel")}: ${u.fullName}`} on={t("activated")} off={t("deactivated")} /></td>
                <td className="whitespace-nowrap text-muted">{date(u.createdAtUtc, locale)}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/admin/users" query={sp} />
    </>
  );
}
