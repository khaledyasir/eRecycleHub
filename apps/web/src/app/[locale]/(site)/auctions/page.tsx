import { SlidersHorizontal, SearchX } from "lucide-react";
import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { AuctionGrid } from "@/components/auction/auction-card";
import { AutoSubmitSelect } from "@/components/auto-submit";
import { EmptyState } from "@/components/ui/display";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { getCountryId, load, serverApi } from "@/lib/api/server";
import { fileUrl, money, nowMs, pick } from "@/lib/format";
import { alternates } from "@/lib/seo";

type Search = Record<string, string | string[] | undefined>;
const one = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v) || undefined;
const many = (v: string | string[] | undefined) => (Array.isArray(v) ? v : v ? [v] : []);
const num = (v: string | undefined) => (v && !Number.isNaN(Number(v)) ? Number(v) : undefined);

const STATUSES = ["", "live", "upcoming", "ended"] as const;
const SORTS = ["newest", "ending", "price_asc", "price_desc", "bids"] as const;
const PERIODS = ["", "today", "yesterday", "last7days", "last30days"] as const;

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("auction.list"), getLocale()]);
  return { title: t("title"), description: t("description"), alternates: alternates(locale, "/auctions") };
}

export default async function AuctionsPage({ searchParams }: { searchParams: Promise<Search> }) {
  const sp = await searchParams;
  const [t, tc, locale, client, countryId] = await Promise.all([getTranslations("auction.list"), getTranslations("common"), getLocale(), serverApi(), getCountryId()]);
  const status = STATUSES.includes(one(sp.status) as never) ? one(sp.status) : undefined;
  const sort = SORTS.includes(one(sp.sort) as never) ? one(sp.sort) : undefined;
  const period = PERIODS.includes(one(sp.period) as never) ? one(sp.period) : undefined;
  const categories = many(sp.category).map(Number).filter(Number.isInteger);
  const query = {
    status, sort, period, q: one(sp.q), category: categories, minPrice: num(one(sp.minPrice)), maxPrice: num(one(sp.maxPrice)),
    page: Math.max(1, num(one(sp.page)) ?? 1), pageSize: 12, countryId,
  };

  const [result, facets, highest] = await Promise.all([
    load(client.GET("/api/auctions", { params: { query } })),
    load(client.GET("/api/auctions/facets", { params: { query: { countryId } } })),
    load(client.GET("/api/auctions", { params: { query: { status: "live", sort: "price_desc", pageSize: 3, countryId } } })),
  ]);
  const serverNow = nowMs();
  const totalPages = Math.ceil(result.total / result.pageSize);
  const statusLabel = { "": t("statusAll"), live: t("statusLive"), upcoming: t("statusUpcoming"), ended: t("statusEnded") };
  const periodLabel = { "": t("periodAny"), today: t("periodToday"), yesterday: t("periodYesterday"), last7days: t("periodWeek"), last30days: t("periodMonth") };
  const periodCount: Record<string, number | undefined> = { today: facets.today, yesterday: facets.yesterday, last7days: facets.last7Days, last30days: facets.last30Days };
  const filtered = !!(status || query.q || categories.length || query.minPrice || query.maxPrice || period);

  const filters = (
    <form action="" className="space-y-6" aria-label={tc("filters")}>
      {sort && <input type="hidden" name="sort" value={sort} />}
      <div>
        <label htmlFor="f-q" className="mb-1.5 block text-sm font-semibold">
          {t("search")}
        </label>
        <input id="f-q" name="q" type="search" defaultValue={query.q} className="h-10 w-full rounded-md border border-line-strong bg-surface px-3 text-sm focus:border-patina-500 focus:outline-none focus:ring-2 focus:ring-patina-100" />
      </div>
      <fieldset>
        <legend className="mb-2 text-sm font-semibold">{t("status")}</legend>
        <div className="space-y-1.5">
          {STATUSES.map((s) => (
            <label key={s} className="flex cursor-pointer items-center gap-2 text-sm">
              <input type="radio" name="status" value={s} defaultChecked={(status ?? "") === s} className="accent-patina" />
              {statusLabel[s]}
            </label>
          ))}
        </div>
      </fieldset>
      {facets.categories.length > 0 && (
        <fieldset>
          <legend className="mb-2 text-sm font-semibold">{t("category")}</legend>
          <div className="space-y-1.5">
            {facets.categories.map((c) => (
              <label key={c.id} className="flex cursor-pointer items-center gap-2 text-sm">
                <input type="checkbox" name="category" value={c.id} defaultChecked={categories.includes(c.id)} className="accent-patina" />
                <span className="flex-1">{pick(locale, c.titleEn, c.titleAr)}</span>
                <span className="tabular text-xs text-muted">{c.count}</span>
              </label>
            ))}
          </div>
        </fieldset>
      )}
      <div>
        <label htmlFor="f-period" className="mb-1.5 block text-sm font-semibold">
          {t("period")}
        </label>
        <select id="f-period" name="period" defaultValue={period ?? ""} className="h-10 w-full rounded-md border border-line-strong bg-surface px-3 text-sm">
          {PERIODS.map((p) => (
            <option key={p} value={p}>
              {periodLabel[p]}
              {p && periodCount[p] !== undefined ? ` (${periodCount[p]})` : ""}
            </option>
          ))}
        </select>
      </div>
      <fieldset>
        <legend className="mb-1.5 text-sm font-semibold">{t("price")}</legend>
        <div className="flex items-center gap-2">
          <label className="sr-only" htmlFor="f-min">{t("minPrice")}</label>
          <input id="f-min" name="minPrice" type="number" min={0} step="0.01" inputMode="decimal" placeholder={t("minPrice")} defaultValue={query.minPrice} className="tabular h-10 w-full rounded-md border border-line-strong bg-surface px-3 text-sm" />
          <span className="text-muted">–</span>
          <label className="sr-only" htmlFor="f-max">{t("maxPrice")}</label>
          <input id="f-max" name="maxPrice" type="number" min={0} step="0.01" inputMode="decimal" placeholder={facets.maxPrice ? String(Math.ceil(facets.maxPrice)) : t("maxPrice")} defaultValue={query.maxPrice} className="tabular h-10 w-full rounded-md border border-line-strong bg-surface px-3 text-sm" />
        </div>
      </fieldset>
      <div className="flex gap-2">
        <button className="h-10 flex-1 rounded-md bg-patina px-4 text-sm font-medium text-white hover:bg-patina-700">{t("showResults")}</button>
        {filtered && (
          <Link href="/auctions" className="inline-flex h-10 items-center rounded-md px-3 text-sm text-ink-2 hover:bg-sunken">
            {tc("clearFilters")}
          </Link>
        )}
      </div>
    </form>
  );

  return (
    <div className="container-page py-8">
      <div className="mb-6">
        <h1 className="text-3xl font-semibold">{t("title")}</h1>
        <p className="mt-2 max-w-[70ch] text-muted">{t("description")}</p>
      </div>
      <div className="grid gap-8 lg:grid-cols-[16rem_1fr]">
        <aside className="space-y-8">
          <details className="rounded-lg border border-line bg-surface p-4 lg:hidden" open={false}>
            <summary className="flex cursor-pointer items-center gap-2 font-medium">
              <SlidersHorizontal className="h-4 w-4" aria-hidden />
              {tc("filters")}
            </summary>
            <div className="mt-4">{filters}</div>
          </details>
          <div className="hidden lg:block">{filters}</div>
          {highest.items.length > 0 && (
            <section className="hidden rounded-lg border border-line bg-surface p-4 lg:block">
              <h2 className="mb-3 text-sm font-semibold">{t("highest")}</h2>
              <ul className="space-y-3">
                {highest.items.map((a) => (
                  <li key={a.id} className="relative flex gap-3">
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    <img src={fileUrl(a.mainImageFileId)} alt="" className="h-12 w-14 shrink-0 rounded object-cover" />
                    <div className="min-w-0">
                      <Link href={`/auctions/${a.id}`} className="line-clamp-2 text-sm font-medium after:absolute after:inset-0">
                        {pick(locale, a.titleEn, a.titleAr)}
                      </Link>
                      <p className="tabular text-sm text-ink-2">{money(a.currentPrice, locale)}</p>
                    </div>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </aside>

        <section aria-labelledby="results-heading">
          <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
            <p id="results-heading" className="tabular font-medium" aria-live="polite">
              {t("results", { count: result.total })}
            </p>
            <form action="" className="flex items-center gap-2">
              {Object.entries({ status, q: query.q, period, minPrice: one(sp.minPrice), maxPrice: one(sp.maxPrice) }).map(([k, v]) => (v ? <input key={k} type="hidden" name={k} value={v} /> : null))}
              {categories.map((c) => (
                <input key={c} type="hidden" name="category" value={c} />
              ))}
              <label htmlFor="sort" className="text-sm text-muted">
                {t("sort")}
              </label>
              <AutoSubmitSelect id="sort" name="sort" defaultValue={sort ?? "newest"} className="h-9 rounded-md border border-line-strong bg-surface px-2 text-sm">
                <option value="newest">{t("sortNewest")}</option>
                <option value="ending">{t("sortEnding")}</option>
                <option value="price_asc">{t("sortPriceAsc")}</option>
                <option value="price_desc">{t("sortPriceDesc")}</option>
                <option value="bids">{t("sortBids")}</option>
              </AutoSubmitSelect>
              <noscript>
                <button className="h-9 rounded-md border border-line-strong px-3 text-sm">{tc("apply")}</button>
              </noscript>
            </form>
          </div>
          {result.items.length === 0 ? (
            <EmptyState
              icon={<SearchX className="h-8 w-8" />}
              title={t("emptyTitle")}
              body={t("emptyBody")}
              action={
                filtered ? (
                  <Link href="/auctions" className="text-sm font-medium text-patina hover:underline">
                    {tc("clearFilters")}
                  </Link>
                ) : undefined
              }
            />
          ) : (
            <AuctionGrid items={result.items} serverNow={serverNow} className="xl:grid-cols-3" />
          )}
          <Pagination page={result.page} totalPages={totalPages} pathname="/auctions" query={sp} />
        </section>
      </div>
    </div>
  );
}
