import { ArrowLeft, Gavel, Search, ShieldCheck, Timer, TrendingUp, Users } from "lucide-react";
import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { AuctionGrid } from "@/components/auction/auction-card";
import { RichHtml } from "@/components/rich-html";
import { Link } from "@/i18n/navigation";
import { getCountryId, load, serverApi } from "@/lib/api/server";
import { fileUrl, nowMs, pick } from "@/lib/format";
import { alternates } from "@/lib/seo";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.home"), getLocale()]);
  return { title: t("metaTitle"), description: t("heroBody"), alternates: alternates(locale, "/") };
}

const accents = [
  { tile: "bg-purple/10 text-purple", link: "text-purple" },
  { tile: "bg-pink/10 text-pink", link: "text-pink-ink" },
  { tile: "bg-orange/10 text-orange", link: "text-orange-ink" },
  { tile: "bg-cyan/20 text-patina", link: "text-patina" },
];

function SectionHead({ kicker, title, href, more }: { kicker?: string; title: string; href?: string; more?: string }) {
  return (
    <div className="mb-9 flex flex-wrap items-end justify-between gap-4">
      <div>
        {kicker && <span className="text-sm font-bold text-patina">{kicker}</span>}
        <h2 className="mt-2 text-2xl font-black sm:text-3xl">{title}</h2>
      </div>
      {href && more && (
        <Link href={href} className="inline-flex shrink-0 items-center gap-2 font-bold text-patina hover:underline">
          {more}
          <ArrowLeft className="h-4 w-4 ltr:rotate-180" aria-hidden />
        </Link>
      )}
    </div>
  );
}

export default async function HomePage() {
  const [t, tc, locale, client, countryId] = await Promise.all([getTranslations("public.home"), getTranslations("common"), getLocale(), serverApi(), getCountryId()]);
  const home = await load(client.GET("/api/home", { params: { query: { countryId } } }));
  const serverNow = nowMs();
  const heroImage = home.live.find((a) => a.mainImageFileId)?.mainImageFileId;
  const stats = [
    { icon: Gavel, tile: "bg-patina-50 text-patina", value: home.stats.liveAuctions, label: t("statLive") },
    { icon: Timer, tile: "bg-orange/10 text-orange", value: home.stats.stores, label: t("statStores") },
    { icon: Users, tile: "bg-purple/10 text-purple", value: home.stats.totalBids, label: t("statBids") },
  ];

  return (
    <>
      <section className="soft-hero relative overflow-hidden">
        <span className="pointer-events-none absolute -end-20 top-20 h-48 w-48 rounded-full bg-pink opacity-[.18]" aria-hidden />
        <span className="pointer-events-none absolute bottom-12 start-10 h-28 w-28 rounded-full bg-orange opacity-[.18]" aria-hidden />
        <span className="pointer-events-none absolute start-1/3 top-8 h-16 w-16 rounded-full bg-purple opacity-[.18]" aria-hidden />
        <div className="container-page relative grid items-center gap-12 py-14 lg:grid-cols-2 lg:py-24">
          <div className="relative z-10">
            <span className="inline-flex items-center gap-2 rounded-full border border-patina-100 bg-surface px-4 py-2 text-xs font-extrabold text-patina shadow-sm sm:text-sm">
              <Gavel className="h-4 w-4" aria-hidden />
              {t("badge")}
            </span>
            <h1 className="mt-7 max-w-[20ch] text-[length:var(--text-display)] font-black leading-[var(--text-display--line-height)]">{t("heroTitle")}</h1>
            <p className="mt-6 max-w-xl text-base leading-9 text-ink-2 sm:text-lg">{t("heroBody")}</p>
            <div className="mt-8 flex flex-wrap gap-3">
              <Link href="/auctions?status=live" className="inline-flex items-center gap-2 rounded-xl bg-patina px-7 py-4 font-extrabold text-white shadow-lg shadow-patina/15 transition hover:-translate-y-1 motion-reduce:transform-none">
                <Gavel className="h-5 w-5" aria-hidden />
                {t("browse")}
              </Link>
              <Link href="/apply" className="inline-flex items-center gap-2 rounded-xl border border-line-strong bg-surface px-7 py-4 font-extrabold transition hover:border-patina">
                <TrendingUp className="h-5 w-5 text-purple" aria-hidden />
                {t("sell")}
              </Link>
            </div>
            <form action="/auctions" role="search" className="mt-9 flex max-w-xl gap-2 rounded-xl border border-line bg-surface p-2 shadow-xl shadow-slate-200/70">
              <label className="flex flex-1 items-center gap-2 px-3">
                <Search className="h-5 w-5 text-muted" aria-hidden />
                <span className="sr-only">{tc("search")}</span>
                <input name="q" type="search" placeholder={tc("searchPlaceholder")} className="w-full bg-transparent py-3 text-sm outline-none" />
              </label>
              <button type="submit" className="rounded-md bg-purple px-6 font-bold text-white hover:brightness-110">
                {t("searchBtn")}
              </button>
            </form>
          </div>

          <div className="relative">
            <div className="absolute -inset-5 rounded-full bg-gradient-to-tr from-cyan/20 via-pink/10 to-orange/20 blur-3xl" aria-hidden />
            <div className="relative rounded-[2rem] border border-white bg-surface p-3 shadow-2xl shadow-slate-300/40 ltr:-rotate-1 rtl:rotate-1">
              {heroImage ? (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={fileUrl(heroImage)} alt="" className="h-[340px] w-full rounded-[1.5rem] object-cover sm:h-[440px]" />
              ) : (
                <div className="grid h-[340px] w-full place-items-center rounded-[1.5rem] bg-patina-50 text-patina sm:h-[440px]" aria-hidden>
                  <Gavel className="h-24 w-24" />
                </div>
              )}
              <div className="absolute inset-x-7 bottom-8 flex items-center justify-between gap-2 rounded-xl bg-surface/95 p-4 shadow-lg backdrop-blur">
                <div>
                  <div className="text-xs text-muted">{t("liveNow", { count: home.stats.liveAuctions })}</div>
                  <div className="text-xl font-black text-patina">{t("heroCardTitle")}</div>
                </div>
                <span className="grid h-12 w-12 place-items-center rounded-full bg-lime/20 text-patina">
                  <TrendingUp aria-hidden />
                </span>
              </div>
            </div>
            <div className="absolute -start-4 -top-5 h-16 w-16 rounded-full bg-orange opacity-80" aria-hidden />
            <div className="absolute -bottom-5 -end-5 h-12 w-12 rounded-full bg-purple opacity-75" aria-hidden />
          </div>
        </div>
      </section>

      <section className="container-page relative z-10 -mt-3">
        <ul className="grid gap-4 sm:grid-cols-3">
          {stats.map(({ icon: Icon, tile, value, label }) => (
            <li key={label} className="flex items-center gap-4 rounded-xl border border-line bg-surface p-6 shadow-sm">
              <span className={`rounded-xl p-4 ${tile}`}>
                <Icon aria-hidden />
              </span>
              <div>
                <div className="tabular text-3xl font-black">{value}</div>
                <div className="text-sm text-muted">{label}</div>
              </div>
            </li>
          ))}
        </ul>
      </section>

      {home.sliders.length > 0 && (
        <section className="container-page mt-12" aria-label={t("announcements")}>
          <div className="flex snap-x snap-mandatory gap-4 overflow-x-auto pb-2">
            {home.sliders.map((s) => {
              const img = (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={fileUrl(s.imageFileId)} alt={s.title ?? ""} className="aspect-[16/5] w-full rounded-xl object-cover" />
              );
              return (
                <div key={s.id} className="w-full shrink-0 snap-start">
                  {s.link ? (
                    s.link.startsWith("/") ? <Link href={s.link}>{img}</Link> : <a href={s.link} rel="noopener noreferrer">{img}</a>
                  ) : (
                    img
                  )}
                </div>
              );
            })}
          </div>
        </section>
      )}

      {home.categories.length > 0 && (
        <section className="container-page py-20">
          <SectionHead kicker={t("exploreKicker")} title={t("categories")} href="/auctions" more={t("seeAll")} />
          <ul className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            {home.categories.slice(0, 8).map((c, i) => {
              const accent = accents[i % accents.length];
              return (
                <li key={c.id}>
                  <Link href={`/auctions?category=${c.id}`} className="block h-full rounded-[1.5rem] border border-line bg-surface p-6 transition hover:-translate-y-1 hover:shadow-pop motion-reduce:transform-none">
                    <span className={`grid h-14 w-14 place-items-center overflow-hidden rounded-xl ${accent.tile}`}>
                      {c.iconFileId ? (
                        // eslint-disable-next-line @next/next/no-img-element
                        <img src={fileUrl(c.iconFileId)} alt="" className="h-9 w-9 object-contain" />
                      ) : (
                        <Gavel aria-hidden />
                      )}
                    </span>
                    <h3 className="mt-6 text-xl font-black">{pick(locale, c.titleEn, c.titleAr)}</h3>
                    <span className={`tabular mt-5 inline-block text-sm font-bold ${accent.link}`}>{c.count}</span>
                  </Link>
                </li>
              );
            })}
          </ul>
        </section>
      )}

      {home.live.length > 0 && (
        <section className="bg-surface py-20">
          <div className="container-page">
            <SectionHead kicker={t("liveKicker")} title={t("live")} href="/auctions?status=live&sort=ending" more={t("seeAll")} />
            <AuctionGrid items={home.live} serverNow={serverNow} />
          </div>
        </section>
      )}

      {home.upcoming.length > 0 && (
        <section className="container-page py-20">
          <SectionHead title={t("upcoming")} href="/auctions?status=upcoming" more={t("seeAll")} />
          <AuctionGrid items={home.upcoming} serverNow={serverNow} />
        </section>
      )}

      <section className="container-page py-20">
        <div className="mb-12 text-center">
          <span className="text-sm font-bold text-patina">{t("stepsKicker")}</span>
          <h2 className="mt-2 text-2xl font-black sm:text-3xl">{t("steps")}</h2>
          <p className="mt-3 text-muted">{t("stepsBody")}</p>
        </div>
        {home.steps.length > 0 ? (
          <ol className="grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
            {home.steps.slice(0, 4).map((s, i) => {
              const accent = accents[i % accents.length];
              return (
                <li key={s.id} className="rounded-[1.5rem] border border-line bg-surface p-7 shadow-sm">
                  <span aria-hidden data-n={String(i + 1).padStart(2, "0")} className={`tabular block h-14 text-5xl font-black before:content-[attr(data-n)] before:opacity-25 ${accent.link}`} />
                  <h3 className="mt-3 text-lg font-black">{pick(locale, s.titleEn, s.titleAr)}</h3>
                  <RichHtml html={pick(locale, s.descEn, s.descAr)} className="mt-2 text-sm leading-7" />
                </li>
              );
            })}
          </ol>
        ) : (
          <ul className="mx-auto grid max-w-4xl gap-5 sm:grid-cols-3">
            {[
              [Gavel, t("rules.minimum")],
              [ShieldCheck, t("rules.insurance")],
              [Timer, t("rules.clock")],
            ].map(([Icon, text], i) => {
              const I = Icon as typeof Gavel;
              return (
                <li key={i} className="rounded-[1.5rem] border border-line bg-surface p-7 shadow-sm">
                  <span className={`grid h-14 w-14 place-items-center rounded-xl ${accents[i].tile}`}>
                    <I aria-hidden />
                  </span>
                  <p className="mt-5 text-sm leading-7 text-ink-2">{text as string}</p>
                </li>
              );
            })}
          </ul>
        )}
      </section>

      {home.announcements.length > 0 && (
        <section className="container-page py-20">
          <SectionHead title={t("announcements")} />
          <ul className="grid gap-4 sm:grid-cols-2">
            {home.announcements.map((a) => (
              <li key={a.id}>
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={fileUrl(a.imageFileId)} alt="" loading="lazy" className="aspect-[16/6] w-full rounded-xl border border-line object-cover" />
              </li>
            ))}
          </ul>
        </section>
      )}

      <section className="container-page pb-20">
        <div className="relative flex flex-col items-start justify-between gap-8 overflow-hidden rounded-[2rem] bg-patina px-7 py-14 text-white sm:px-14 md:flex-row md:items-center">
          <div className="absolute -top-16 start-1/3 h-56 w-56 rounded-full bg-cyan/20" aria-hidden />
          <div className="relative">
            <h2 className="text-2xl font-black sm:text-3xl">{t("sellerTitle")}</h2>
            <p className="mt-3 max-w-[60ch] text-white">{t("sellerBody")}</p>
          </div>
          <Link href="/apply" className="relative inline-flex shrink-0 items-center rounded-xl bg-surface px-8 py-4 font-black text-patina transition hover:bg-patina-50">
            {t("sellerCta")}
          </Link>
        </div>
      </section>
    </>
  );
}
