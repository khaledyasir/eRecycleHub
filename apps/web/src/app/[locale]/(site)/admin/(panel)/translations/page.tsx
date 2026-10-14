import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { TranslationRow } from "@/components/admin/cms-extras";
import { Alert, EmptyState, PageHeader } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { shippedTexts } from "@/lib/catalogue";
import { cn } from "@/lib/format";

const PAGE_SIZE = 25;

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.translations"))("title") };
}

export default async function Translations({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const loc = sp.locale === "ar" ? "ar" : "en";
  const q = sp.q?.trim().toLowerCase() ?? "";
  const area = sp.area ?? "";
  const [t, client] = await Promise.all([getTranslations("cms.translations"), serverApi()]);
  const overrides = new Map((await load(client.GET("/api/admin/translations", { params: { query: { locale: loc } } }))).map((o) => [o.key, o.value]));
  const all = Object.entries(shippedTexts(loc));
  const areas = [...new Set(all.map(([k]) => k.split(".")[0]))];
  const hits = all.filter(([k, v]) => (!area || k.startsWith(`${area}.`)) && (!q || k.toLowerCase().includes(q) || v.toLowerCase().includes(q) || overrides.get(k)?.toLowerCase().includes(q)));
  const totalPages = Math.max(1, Math.ceil(hits.length / PAGE_SIZE));
  const page = Math.min(Math.max(1, Number(sp.page) || 1), totalPages);
  const rows = hits.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);
  const input = "h-9 rounded-md border border-line-strong bg-surface px-2 text-sm";
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <Alert tone="warn">{t("placeholders")}</Alert>
      <nav className="mb-4 mt-4 flex gap-1 border-b border-line" aria-label={t("locale")}>
        {(["en", "ar"] as const).map((l) => (
          <Link key={l} href={`/admin/translations?locale=${l}`} aria-current={loc === l ? "page" : undefined} className={cn("-mb-px border-b-2 px-3 py-2.5 text-sm font-medium", loc === l ? "border-patina text-ink" : "border-transparent text-muted hover:text-ink")}>
            {l === "en" ? "English" : "العربية"}
          </Link>
        ))}
      </nav>
      <form className="mb-3 flex flex-wrap items-end gap-2" role="search">
        <input type="hidden" name="locale" value={loc} />
        <label className="flex flex-col gap-1 text-xs text-muted">{t("search")}<input name="q" type="search" defaultValue={sp.q} className={cn(input, "w-64")} /></label>
        <label className="flex flex-col gap-1 text-xs text-muted">{t("area")}
          <select name="area" defaultValue={area} className={input}>
            <option value="">{t("allAreas")}</option>
            {areas.map((a) => <option key={a} value={a}>{a}</option>)}
          </select>
        </label>
        <button className="h-9 rounded-md bg-ink px-3 text-sm font-medium text-white">{t("search")}</button>
        <span className="pb-2 text-xs text-muted">{t("count", { count: hits.length })}</span>
      </form>
      {rows.length === 0 ? (
        <EmptyState title={t("empty")} />
      ) : (
        <Table>
          <thead><tr><th>{t("key")}</th><th>{t("default")}</th><th>{t("override")}</th></tr></thead>
          <tbody>
            {rows.map(([k, v]) => <TranslationRow key={`${loc}:${k}`} locale={loc} textKey={k} shipped={v} override={overrides.get(k) ?? null} />)}
          </tbody>
        </Table>
      )}
      <Pagination page={page} totalPages={totalPages} pathname="/admin/translations" query={sp} />
    </>
  );
}
