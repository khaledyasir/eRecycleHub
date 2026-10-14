import { ArrowUpRight, Gavel, ShieldCheck, Timer } from "lucide-react";
import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { AuctionGrid } from "@/components/auction/auction-card";
import { Countdown } from "@/components/auction/countdown";
import { RichHtml } from "@/components/rich-html";
import { StoreCard } from "@/components/store-card";
import { Link } from "@/i18n/navigation";
import { getCountryId, load, serverApi } from "@/lib/api/server";
import { fileUrl, money, nowMs, pick } from "@/lib/format";
import { alternates } from "@/lib/seo";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.home"), getLocale()]);
  return { title: t("metaTitle"), description: t("heroBody"), alternates: alternates(locale, "/") };
}

function SectionHead({ title, href, more }: { title: string; href?: string; more?: string }) {
  return (
    <div className="mb-5 flex items-end justify-between gap-4">
      <h2 className="text-xl font-semibold sm:text-2xl">{title}</h2>
      {href && more && (
        <Link href={href} className="inline-flex shrink-0 items-center gap-1 text-sm font-medium text-patina hover:underline">
          {more}
          <ArrowUpRight className="flip-rtl h-4 w-4" aria-hidden />
        </Link>
      )}
    </div>
  );
}

export default async function HomePage() {
  const [t, locale, client, countryId] = await Promise.all([getTranslations("public.home"), getLocale(), serverApi(), getCountryId()]);
  const home = await load(client.GET("/api/home", { params: { query: { countryId } } }));
  const serverNow = nowMs();
  const closing = home.live.slice(0, 5);

  return (
    <>
      {/* Hero: the claim on one side, the real "closing next" board on the other. */}
      <section className="border-b border-line bg-surface">
        <div className="container-page grid gap-10 py-10 lg:grid-cols-[1.1fr_1fr] lg:py-14">
          <div className="flex flex-col justify-center">
            <h1 className="max-w-[18ch] text-[length:var(--text-display)] font-semibold leading-[var(--text-display--line-height)] tracking-[var(--text-display--letter-spacing)]">
              {t("heroTitle")}
            </h1>
            <p className="mt-5 max-w-[60ch] text-lg text-ink-2">{t("heroBody")}</p>
            <div className="mt-7 flex flex-wrap gap-3">
              <Link href="/auctions?status=live" className="inline-flex h-12 items-center gap-2 rounded-md bg-patina px-5 font-medium text-white hover:bg-patina-700">
                <Gavel className="h-5 w-5" aria-hidden />
                {t("browse")}
              </Link>
              <Link href="/apply" className="inline-flex h-12 items-center rounded-md border border-line-strong bg-surface px-5 font-medium hover:bg-sunken">
                {t("sell")}
              </Link>
            </div>
            <p className="tabular mt-6 text-sm text-muted">{t("stats", { live: home.stats.liveAuctions, stores: home.stats.stores, bids: home.stats.totalBids })}</p>
          </div>

          <div className="rounded-xl border border-ink/10 bg-ink p-1 text-white shadow-pop">
            <div className="flex items-center justify-between px-4 py-3">
              <h2 className="flex items-center gap-2 font-semibold">
                <Timer className="h-5 w-5 text-hivis" aria-hidden />
                {t("closingNext")}
              </h2>
              <span className="tabular text-sm text-white/60">{t("liveNow", { count: home.stats.liveAuctions })}</span>
            </div>
            {closing.length === 0 ? (
              <p className="px-4 pb-5 text-sm text-white/70">{t("closingEmpty")}</p>
            ) : (
              <ol className="divide-y divide-white/10 overflow-hidden rounded-lg bg-white/[0.04]">
                {closing.map((a) => (
                  <li key={a.id} className="relative flex items-center gap-3 px-3 py-3 hover:bg-white/[0.06]">
                    <div className="h-12 w-16 shrink-0 overflow-hidden rounded-md bg-white/10">
                      {a.mainImageFileId && (
                        // eslint-disable-next-line @next/next/no-img-element
                        <img src={fileUrl(a.mainImageFileId)} alt="" className="h-full w-full object-cover" />
                      )}
                    </div>
                    <div className="min-w-0 flex-1">
                      <Link href={`/auctions/${a.id}`} className="line-clamp-1 font-medium after:absolute after:inset-0">
                        {pick(locale, a.titleEn, a.titleAr)}
                      </Link>
                      <p className="tabular text-sm text-white/70">{money(a.currentPrice, locale)}</p>
                    </div>
                    <Countdown target={a.endsAtUtc} serverNow={serverNow} className="shrink-0 rounded-sm bg-hivis px-2 py-1 text-sm text-hivis-ink" />
                  </li>
                ))}
              </ol>
            )}
          </div>
        </div>
      </section>

      {home.sliders.length > 0 && (
        <section className="container-page mt-8" aria-label={t("announcements")}>
          <div className="flex snap-x snap-mandatory gap-4 overflow-x-auto pb-2">
            {home.sliders.map((s) => {
              const img = (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={fileUrl(s.imageFileId)} alt={s.title ?? ""} className="aspect-[16/5] w-full rounded-lg object-cover" />
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

      {home.live.length > 0 && (
        <section className="container-page mt-14">
          <SectionHead title={t("live")} href="/auctions?status=live&sort=ending" more={t("seeAll")} />
          <AuctionGrid items={home.live} serverNow={serverNow} />
        </section>
      )}

      {home.categories.length > 0 && (
        <section className="container-page mt-14">
          <SectionHead title={t("categories")} />
          <ul className="flex flex-wrap gap-2">
            {home.categories.map((c) => (
              <li key={c.id}>
                <Link href={`/auctions?category=${c.id}`} className="inline-flex h-11 items-center gap-2 rounded-md border border-line bg-surface pe-3 ps-1.5 text-sm font-medium hover:border-patina-500">
                  {c.iconFileId && (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img src={fileUrl(c.iconFileId)} alt="" className="h-8 w-8 rounded" />
                  )}
                  {pick(locale, c.titleEn, c.titleAr)}
                  <span className="tabular rounded-sm bg-sunken px-1.5 text-xs text-ink-2">{c.count}</span>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      {home.upcoming.length > 0 && (
        <section className="container-page mt-14">
          <SectionHead title={t("upcoming")} href="/auctions?status=upcoming" more={t("seeAll")} />
          <AuctionGrid items={home.upcoming} serverNow={serverNow} />
        </section>
      )}

      <section className="container-page mt-14 grid gap-8 lg:grid-cols-[1fr_1.2fr]">
        <div>
          <h2 className="text-xl font-semibold sm:text-2xl">{t("steps")}</h2>
          <p className="mt-2 text-muted">{t("stepsBody")}</p>
          <ul className="mt-6 space-y-4">
            {[
              [Gavel, t("rules.minimum")],
              [ShieldCheck, t("rules.insurance")],
              [Timer, t("rules.clock")],
            ].map(([Icon, text], i) => {
              const I = Icon as typeof Gavel;
              return (
                <li key={i} className="flex gap-3">
                  <I className="mt-0.5 h-5 w-5 shrink-0 text-patina" aria-hidden />
                  <span className="text-ink-2">{text as string}</span>
                </li>
              );
            })}
          </ul>
        </div>
        {home.steps.length > 0 && (
          <ol className="grid gap-3 sm:grid-cols-2">
            {home.steps.map((s, i) => (
              <li key={s.id} className="rounded-lg border border-line bg-surface p-4">
                <span className="tabular inline-flex h-7 w-7 items-center justify-center rounded-full bg-patina text-sm font-semibold text-white">{i + 1}</span>
                <h3 className="mt-3 font-semibold">{pick(locale, s.titleEn, s.titleAr)}</h3>
                <RichHtml html={pick(locale, s.descEn, s.descAr)} className="mt-1 text-sm" />
              </li>
            ))}
          </ol>
        )}
      </section>

      {home.stores.length > 0 && (
        <section className="container-page mt-14">
          <SectionHead title={t("stores")} href="/stores" more={t("seeAll")} />
          <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {home.stores.map((s) => (
              <li key={s.id} className="flex">
                <div className="w-full">
                  <StoreCard s={s} />
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {home.announcements.length > 0 && (
        <section className="container-page mt-14">
          <SectionHead title={t("announcements")} />
          <ul className="grid gap-4 sm:grid-cols-2">
            {home.announcements.map((a) => (
              <li key={a.id}>
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={fileUrl(a.imageFileId)} alt="" loading="lazy" className="aspect-[16/6] w-full rounded-lg border border-line object-cover" />
              </li>
            ))}
          </ul>
        </section>
      )}

      {home.ended.length > 0 && (
        <section className="container-page mt-14">
          <SectionHead title={t("ended")} href="/auctions?status=ended" more={t("seeAll")} />
          <AuctionGrid items={home.ended} serverNow={serverNow} />
        </section>
      )}

      <section className="container-page mt-16">
        <div className="flex flex-col items-start justify-between gap-6 rounded-xl bg-patina px-6 py-8 text-white sm:flex-row sm:items-center sm:px-10">
          <div>
            <h2 className="text-2xl font-semibold">{t("sellerTitle")}</h2>
            <p className="mt-2 max-w-[60ch] text-white/80">{t("sellerBody")}</p>
          </div>
          <Link href="/apply" className="inline-flex h-12 shrink-0 items-center rounded-md bg-hivis px-5 font-semibold text-hivis-ink hover:brightness-95">
            {t("sellerCta")}
          </Link>
        </div>
      </section>
    </>
  );
}
