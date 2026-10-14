import { Mail, MapPin, Phone, SearchX, Star, Store } from "lucide-react";
import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getLocale, getTranslations } from "next-intl/server";
import { AuctionGrid } from "@/components/auction/auction-card";
import { AutoSubmitSelect } from "@/components/auto-submit";
import { EmptyState } from "@/components/ui/display";
import { Pagination } from "@/components/ui/pagination";
import { load, serverApi } from "@/lib/api/server";
import { fileUrl, nowMs, pick } from "@/lib/format";
import { alternates } from "@/lib/seo";

type Params = { params: Promise<{ id: string; locale: string }>; searchParams: Promise<Record<string, string | undefined>> };

async function getStore(id: string) {
  const n = Number(id);
  if (!Number.isInteger(n) || n <= 0) notFound();
  return load((await serverApi()).GET("/api/stores/{id}", { params: { path: { id: n } } }));
}

export async function generateMetadata({ params }: Params): Promise<Metadata> {
  const { id, locale } = await params;
  const s = await getStore(id);
  return { title: s.store.name, alternates: alternates(locale, `/stores/${s.store.id}`) };
}

export default async function StorePage({ params, searchParams }: Params) {
  const [{ id }, sp] = await Promise.all([params, searchParams]);
  const [s, t, ta, tc, locale, client] = await Promise.all([
    getStore(id), getTranslations("public.stores"), getTranslations("auction.list"), getTranslations("common"), getLocale(), serverApi(),
  ]);
  const status = ["live", "upcoming", "ended"].includes(sp.status ?? "") ? sp.status : undefined;
  const page = Math.max(1, Number(sp.page) || 1);
  // countryId 0 = all countries: a store page shows all of the store's lots regardless of the visitor's country.
  const lots = await load(client.GET("/api/auctions", { params: { query: { storeId: s.store.id, status, q: sp.q, page, pageSize: 12, countryId: 0 } } }));
  const serverNow = nowMs();
  const country = pick(locale, s.countryNameEn, s.countryNameAr);
  const phones = [s.store.phone, s.store.secondMobile].filter((p): p is string => !!p);
  return (
    <div>
      <div className="h-40 bg-patina-900 sm:h-56">
        {s.store.backgroundFileId && (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={fileUrl(s.store.backgroundFileId)} alt="" className="h-full w-full object-cover" />
        )}
      </div>
      <div className="container-page">
        <div className="-mt-12 flex flex-col gap-5 rounded-xl border border-line bg-surface p-5 sm:flex-row sm:items-end">
          <div className="h-24 w-24 shrink-0 overflow-hidden rounded-xl border-4 border-surface bg-sunken shadow-raise">
            {s.store.logoFileId ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img src={fileUrl(s.store.logoFileId)} alt="" className="h-full w-full object-cover" />
            ) : (
              <Store className="m-7 h-8 w-8 text-muted" aria-hidden />
            )}
          </div>
          <div className="min-w-0 flex-1">
            <h1 className="text-2xl font-semibold">{s.store.name}</h1>
            <p className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-ink-2">
              <span className="tabular">{t("lots", { count: s.store.auctionCount })}</span>
              <span className="inline-flex items-center gap-1">
                <Star className="h-4 w-4 fill-hivis text-hivis" aria-hidden />
                {s.ratingCount > 0 ? t("rating", { rating: s.ratingAverage, count: s.ratingCount }) : t("noRating")}
              </span>
              {country && <span>{country}</span>}
            </p>
          </div>
          <address className="space-y-1 text-sm not-italic text-ink-2">
            {s.store.address && (
              <p className="flex items-center gap-2">
                <MapPin className="h-4 w-4 text-muted" aria-hidden />
                {s.store.address}
              </p>
            )}
            {phones.map((p) => (
              <p key={p} className="flex items-center gap-2">
                <Phone className="h-4 w-4 text-muted" aria-hidden />
                <a href={`tel:${p.replace(/\s/g, "")}`} className="hover:underline" dir="ltr">
                  {p}
                </a>
              </p>
            ))}
            {s.email && (
              <p className="flex items-center gap-2">
                <Mail className="h-4 w-4 text-muted" aria-hidden />
                <a href={`mailto:${s.email}`} className="hover:underline">
                  {s.email}
                </a>
              </p>
            )}
          </address>
        </div>

        <section className="mt-10" aria-labelledby="lots">
          <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
            <h2 id="lots" className="text-xl font-semibold">
              {t("lotsTitle")}
            </h2>
            <form className="flex flex-wrap items-center gap-2">
              <label htmlFor="sq" className="sr-only">
                {ta("search")}
              </label>
              <input id="sq" name="q" type="search" defaultValue={sp.q} placeholder={ta("search")} className="h-9 rounded-md border border-line-strong bg-surface px-3 text-sm" />
              <label htmlFor="ss" className="sr-only">
                {ta("status")}
              </label>
              <AutoSubmitSelect id="ss" name="status" defaultValue={status ?? ""} className="h-9 rounded-md border border-line-strong bg-surface px-2 text-sm">
                <option value="">{ta("statusAll")}</option>
                <option value="live">{ta("statusLive")}</option>
                <option value="upcoming">{ta("statusUpcoming")}</option>
                <option value="ended">{ta("statusEnded")}</option>
              </AutoSubmitSelect>
              <button className="h-9 rounded-md border border-line-strong bg-surface px-3 text-sm hover:bg-sunken">{tc("search")}</button>
            </form>
          </div>
          {lots.items.length === 0 ? <EmptyState icon={<SearchX className="h-8 w-8" />} title={t("lotsEmpty")} /> : <AuctionGrid items={lots.items} serverNow={serverNow} />}
          <Pagination page={lots.page} totalPages={Math.ceil(lots.total / lots.pageSize)} pathname={`/stores/${s.store.id}`} query={sp} />
        </section>
      </div>
    </div>
  );
}
