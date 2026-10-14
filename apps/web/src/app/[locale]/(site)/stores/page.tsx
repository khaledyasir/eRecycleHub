import { Search, Store } from "lucide-react";
import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { StoreCard } from "@/components/store-card";
import { EmptyState } from "@/components/ui/display";
import { Pagination } from "@/components/ui/pagination";
import { getCountryId, load, serverApi } from "@/lib/api/server";
import { alternates } from "@/lib/seo";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.stores"), getLocale()]);
  return { title: t("title"), description: t("description"), alternates: alternates(locale, "/stores") };
}

export default async function StoresPage({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, client, countryId] = await Promise.all([getTranslations("public.stores"), serverApi(), getCountryId()]);
  const page = Math.max(1, Number(sp.page) || 1);
  const result = await load(client.GET("/api/stores", { params: { query: { q: sp.q, page, countryId } } }));
  return (
    <div className="container-page py-8">
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-3xl font-semibold">{t("title")}</h1>
          <p className="mt-2 text-muted">{t("description")}</p>
        </div>
        <form role="search" className="relative w-full max-w-xs">
          <label htmlFor="store-q" className="sr-only">
            {t("search")}
          </label>
          <Search className="pointer-events-none absolute start-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden />
          <input
            id="store-q"
            name="q"
            type="search"
            defaultValue={sp.q}
            placeholder={t("search")}
            className="h-10 w-full rounded-md border border-line-strong bg-surface ps-9 pe-3 text-sm focus:border-patina-500 focus:outline-none focus:ring-2 focus:ring-patina-100"
          />
        </form>
      </div>
      {result.items.length === 0 ? (
        <EmptyState icon={<Store className="h-8 w-8" />} title={t("emptyTitle")} body={t("emptyBody")} />
      ) : (
        <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {result.items.map((s) => (
            <li key={s.id}>
              <StoreCard s={s} />
            </li>
          ))}
        </ul>
      )}
      <Pagination page={result.page} totalPages={Math.ceil(result.total / result.pageSize)} pathname="/stores" query={sp} />
    </div>
  );
}
