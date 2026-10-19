import { ArrowLeft, CirclePlay, Gavel, Layers, Leaf, Recycle, Search, ShieldCheck, Timer, TrendingUp, Users } from "lucide-react";
import type { Metadata } from "next";
import type { ReactNode } from "react";
import { getLocale, getTranslations } from "next-intl/server";
import { AuctionGrid } from "@/components/auction/auction-card";
import { RichHtml } from "@/components/rich-html";
import { Link } from "@/i18n/navigation";
import { getCountryId, load, loadOptional, serverApi } from "@/lib/api/server";
import type { Schema } from "@/lib/api/types";
import { fileUrl, nowMs, pick } from "@/lib/format";
import { parseHeroTitle, type HeroTone } from "@/lib/hero-title";
import { alternates } from "@/lib/seo";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.home"), getLocale()]);
  return { title: t("metaTitle"), description: t("heroBody"), alternates: alternates(locale, "/") };
}

/** Admin-editable heading/body per homepage section: settings "home-<section>" (title = heading, value = text); built-in text when absent. */
const SECTIONS = ["categories", "live", "upcoming", "steps", "seller"] as const;
const DEFAULT_HERO_IMAGE = "/images/recycling-hero.svg";

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

const heroTones: Record<HeroTone, string | undefined> = { plain: undefined, green: "text-patina", purple: "text-purple" };

/** Hero button: site paths go through the locale-aware Link, full URLs (set by an admin) are plain links. */
function HeroLink({ href, className, children }: { href: string; className: string; children: ReactNode }) {
  return href.startsWith("/") ? (
    <Link href={href} className={className}>{children}</Link>
  ) : (
    <a href={href} rel="noopener noreferrer" className={className}>{children}</a>
  );
}

export default async function HomePage() {
  const [t, tc, locale, client, countryId] = await Promise.all([getTranslations("public.home"), getTranslations("common"), getLocale(), serverApi(), getCountryId()]);
  const [home, ...rows] = await Promise.all([
    load(client.GET("/api/home", { params: { query: { countryId } } })),
    ...SECTIONS.map((key) => loadOptional<Schema<"SettingDto">>(client.GET("/api/content/settings/{key}", { params: { path: { key: `home-${key}` } } }))),
  ]);
  const sec = Object.fromEntries(SECTIONS.map((key, i) => [key, { title: pick(locale, rows[i]?.titleEn, rows[i]?.titleAr) || undefined, body: pick(locale, rows[i]?.valueEn, rows[i]?.valueAr) || undefined }])) as Record<(typeof SECTIONS)[number], { title?: string; body?: string }>;
  const serverNow = nowMs();
  // Homepage hero edited in the admin panel; each empty field keeps the built-in text in the page's language (never the other
  // language's custom text), no record keeps the built-in hero.
  const hero = home.hero;
  const custom = (en?: string | null, ar?: string | null) => (locale === "ar" ? ar : en) || undefined;
  const heroTitle = custom(hero?.titleEn, hero?.titleAr);
  const heroSrc = hero?.imageFileId ? fileUrl(hero.imageFileId) : DEFAULT_HERO_IMAGE;
  const stats = [
    { icon: Gavel, tile: "bg-patina-50 text-patina", value: home.stats.liveAuctions, label: t("statLive") },
    { icon: Layers, tile: "bg-orange/10 text-orange", value: home.categories.length, label: t("statCategories") },
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
            <span className="inline-flex items-center gap-2 rounded-full border border-patina-100 bg-surface/90 px-4 py-2 text-xs font-extrabold text-patina shadow-sm shadow-patina/5 sm:px-5 sm:text-sm">
              <Leaf className="h-4 w-4 shrink-0" aria-hidden />
              {custom(hero?.badgeEn, hero?.badgeAr) ?? t("badge")}
            </span>
            <h1 className="mt-6 text-[length:var(--text-display)] font-black leading-[var(--text-display--line-height)] text-ink sm:mt-8">
              {heroTitle
                ? parseHeroTitle(heroTitle).map((line, i) => (
                    <span key={i} className={i > 0 ? "block" : undefined}>
                      {line.map((s, j) => <span key={j} className={heroTones[s.tone]}>{s.text}</span>)}
                    </span>
                  ))
                : t.rich("heroTitle", {
                    hl: (chunks) => <span className="text-patina">{chunks}</span>,
                    dot: (chunks) => <span className="text-orange">{chunks}</span>,
                    line: (chunks) => <span className="block">{chunks}</span>,
                    accent: (chunks) => <span className="text-purple">{chunks}</span>,
                  })}
            </h1>
            <p className="mt-5 max-w-xl whitespace-pre-line text-base leading-8 text-ink-2 sm:mt-6 sm:text-lg sm:leading-9">{custom(hero?.leadEn, hero?.leadAr) ?? t("heroLead")}</p>
            <div className="mt-8 flex flex-col gap-3 sm:mt-9 sm:flex-row sm:flex-wrap sm:gap-4">
              <HeroLink href={hero?.primaryCtaUrl || "/auctions?status=live"} className="inline-flex items-center justify-center gap-2.5 rounded-2xl bg-patina px-8 py-4 font-extrabold text-white shadow-lg shadow-patina/25 transition hover:-translate-y-0.5 hover:bg-patina-700 motion-reduce:transform-none">
                <Gavel className="h-5 w-5" aria-hidden />
                {custom(hero?.primaryCtaEn, hero?.primaryCtaAr) ?? t("heroBrowse")}
              </HeroLink>
              <HeroLink href={hero?.secondaryCtaUrl || "/how-to-buy"} className="inline-flex items-center justify-center gap-2.5 rounded-2xl border border-line-strong/70 bg-surface px-8 py-4 font-extrabold text-ink transition hover:-translate-y-0.5 hover:border-purple/40 motion-reduce:transform-none">
                <CirclePlay className="h-5 w-5 text-purple" aria-hidden />
                {custom(hero?.secondaryCtaEn, hero?.secondaryCtaAr) ?? t("heroHow")}
              </HeroLink>
            </div>
            {(hero?.showSearch ?? true) && (
              <form action="/auctions" role="search" className="mt-9 flex max-w-xl items-center gap-2 rounded-2xl border border-line bg-surface p-2 shadow-xl shadow-slate-200/60 sm:mt-10">
                <label className="flex min-w-0 flex-1 items-center gap-2.5 px-3">
                  <Search className="h-5 w-5 shrink-0 text-muted" aria-hidden />
                  <span className="sr-only">{tc("search")}</span>
                  <input name="q" type="search" placeholder={t("searchPlaceholder")} className="w-full min-w-0 bg-transparent py-3 text-sm outline-none placeholder:text-muted" />
                </label>
                <button type="submit" className="shrink-0 rounded-xl bg-purple px-6 py-3 font-extrabold text-white transition hover:brightness-110 sm:px-8">
                  {t("searchBtn")}
                </button>
              </form>
            )}
          </div>

          <div className="relative">
            <div className="absolute -inset-5 rounded-full bg-gradient-to-tr from-cyan/20 via-pink/10 to-orange/20 blur-3xl" aria-hidden />
            <div className="relative rounded-[2rem] border border-white bg-surface p-3 shadow-2xl shadow-slate-300/40 ltr:-rotate-1 rtl:rotate-1">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={heroSrc} alt={(hero?.imageFileId && custom(hero.imageAltEn, hero.imageAltAr)) || t("heroImageAlt")} fetchPriority="high" className="h-[340px] w-full rounded-[1.5rem] object-cover sm:h-[440px]" />
              <div className="absolute inset-x-7 bottom-8 flex items-center justify-between gap-2 rounded-xl bg-surface/95 p-4 shadow-lg backdrop-blur">
                <div>
                  <div className="text-xs text-muted">{t("liveNow", { count: home.stats.liveAuctions })}</div>
                  <div className="text-xl font-black text-patina">{custom(hero?.cardTitleEn, hero?.cardTitleAr) ?? t("heroCardTitle")}</div>
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
          <SectionHead kicker={t("exploreKicker")} title={sec.categories.title ?? t("categories")} href="/auctions" more={t("seeAll")} />
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
            <SectionHead kicker={t("liveKicker")} title={sec.live.title ?? t("live")} href="/auctions?status=live&sort=ending" more={t("seeAll")} />
            <AuctionGrid items={home.live} serverNow={serverNow} />
          </div>
        </section>
      )}

      {home.upcoming.length > 0 && (
        <section className="container-page py-20">
          <SectionHead title={sec.upcoming.title ?? t("upcoming")} href="/auctions?status=upcoming" more={t("seeAll")} />
          <AuctionGrid items={home.upcoming} serverNow={serverNow} />
        </section>
      )}

      <section className="container-page py-20">
        <div className="mb-12 text-center">
          <span className="text-sm font-bold text-patina">{t("stepsKicker")}</span>
          <h2 className="mt-2 text-2xl font-black sm:text-3xl">{sec.steps.title ?? t("steps")}</h2>
          <p className="mt-3 text-muted">{sec.steps.body ?? t("stepsBody")}</p>
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
          <Recycle className="absolute -bottom-10 -end-8 h-56 w-56 text-white/10" aria-hidden />
          <div className="relative">
            <h2 className="text-2xl font-black sm:text-3xl">{sec.seller.title ?? t("sellerTitle")}</h2>
            <p className="mt-3 max-w-[60ch] text-white">{sec.seller.body ?? t("sellerBody")}</p>
          </div>
          <Link href="/apply" className="relative inline-flex shrink-0 items-center rounded-xl bg-surface px-8 py-4 font-black text-patina transition hover:bg-patina-50">
            {t("sellerCta")}
          </Link>
        </div>
      </section>
    </>
  );
}
