import { CalendarClock, ChevronRight, FileText, PlayCircle, Store } from "lucide-react";
import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getLocale, getTranslations } from "next-intl/server";
import { AuctionGrid, PhaseBadge } from "@/components/auction/auction-card";
import { BidPanel } from "@/components/auction/bid-panel";
import { Gallery } from "@/components/auction/gallery";
import { FavoriteButton, WatchButton } from "@/components/auction/toggles";
import { RichHtml } from "@/components/rich-html";
import { Alert } from "@/components/ui/display";
import { Tabs } from "@/components/ui/controls";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { CURRENCY, dateTime, fileUrl, nowMs, pick } from "@/lib/format";
import { alternates, excerpt, siteUrl } from "@/lib/seo";

type Params = { params: Promise<{ id: string; locale: string }> };

async function getAuction(id: string) {
  const n = Number(id);
  if (!Number.isInteger(n) || n <= 0) notFound();
  const client = await serverApi();
  return load(client.GET("/api/auctions/{id}", { params: { path: { id: n } } }));
}

export async function generateMetadata({ params }: Params): Promise<Metadata> {
  const { id, locale } = await params;
  const a = await getAuction(id);
  const title = pick(locale, a.card.titleEn, a.card.titleAr);
  const description = excerpt(pick(locale, a.card.shortDescEn, a.card.shortDescAr) || pick(locale, a.descEn, a.descAr));
  return {
    title,
    description,
    alternates: alternates(locale, `/auctions/${a.card.id}`),
    openGraph: { title, description, images: a.card.mainImageFileId ? [{ url: fileUrl(a.card.mainImageFileId)! }] : undefined },
    robots: a.card.isActive ? undefined : { index: false },
  };
}

export default async function AuctionPage({ params }: Params) {
  const { id } = await params;
  const [a, t, tc, locale] = await Promise.all([getAuction(id), getTranslations("auction.detail"), getTranslations("common"), getLocale()]);
  const serverNow = nowMs();
  const textDir = locale === "ar" ? "rtl" : "ltr";
  const c = a.card;
  const title = pick(locale, c.titleEn, c.titleAr);
  const description = pick(locale, a.descEn, a.descAr);
  const days = c.endsAtUtc ? Math.max(1, Math.round((new Date(c.endsAtUtc).getTime() - new Date(c.startsAtUtc).getTime()) / 86_400_000)) : null;
  const photos = [c.mainImageFileId, ...a.images.map((i) => i.fileId)].filter((x): x is string => !!x);
  const docs = [
    [t("docTerms"), a.terms],
    [t("docPayment"), a.paymentConditions],
    [t("docContract"), a.contract],
  ] as const;
  const hasDocs = docs.some(([, d]) => d);

  // schema.org Product + Offer: the closest standard vocabulary for a lot with a current price.
  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "Product",
    name: title,
    sku: c.refNumber,
    image: c.mainImageFileId ? `${siteUrl()}${fileUrl(c.mainImageFileId)}` : undefined,
    description: excerpt(description, 300) || undefined,
    offers: {
      "@type": "Offer",
      priceCurrency: CURRENCY,
      price: c.currentPrice.toFixed(2),
      availability: c.phase === "Ended" ? "https://schema.org/SoldOut" : "https://schema.org/InStock",
      url: `${siteUrl()}/${locale}/auctions/${c.id}`,
      ...(c.endsAtUtc ? { priceValidUntil: c.endsAtUtc.slice(0, 10) } : {}),
    },
  };

  const tabs = [
    {
      value: "description",
      label: t("description"),
      content: description ? <RichHtml html={description} /> : <p className="text-sm text-muted">{t("noDescription")}</p>,
    },
    ...(a.specs.length
      ? [
          {
            value: "specs",
            label: t("specs"),
            content: (
              <dl className="divide-y divide-line overflow-hidden rounded-lg border border-line bg-surface">
                {a.specs.map((s) => (
                  <div key={s.id} className="grid gap-1 px-4 py-2.5 text-sm sm:grid-cols-[14rem_1fr]">
                    <dt className="text-muted">{pick(locale, s.titleEn, s.titleAr)}</dt>
                    <dd className="font-medium">{pick(locale, s.valueEn, s.valueAr) || "—"}</dd>
                  </div>
                ))}
              </dl>
            ),
          },
        ]
      : []),
    {
      value: "documents",
      label: t("documents"),
      content: hasDocs ? (
        <ul className="space-y-3">
          {docs.map(([label, d]) =>
            d ? (
              <li key={label} className="rounded-lg border border-line bg-surface">
                <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
                  <span className="inline-flex items-center gap-2 font-medium">
                    <FileText className="h-5 w-5 text-patina" aria-hidden /> {label}
                  </span>
                  <a href={fileUrl(d.id)} target="_blank" rel="noopener" className="text-sm font-medium text-patina hover:underline">
                    {t("docOpen")}
                  </a>
                </div>
                {d.contentType === "application/pdf" && (
                  <details className="border-t border-line">
                    <summary className="cursor-pointer px-4 py-2 text-sm text-ink-2">{d.name}</summary>
                    <iframe src={fileUrl(d.id)} title={label} className="h-[70vh] w-full border-0" loading="lazy" />
                  </details>
                )}
              </li>
            ) : null,
          )}
        </ul>
      ) : (
        <p className="text-sm text-muted">{t("noDocuments")}</p>
      ),
    },
  ];

  return (
    <div className="container-page py-6">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd).replace(/</g, "\\u003c") }} />
      <nav aria-label="breadcrumb" className="mb-4 text-sm text-muted" dir={textDir}>
        <ol className="flex flex-wrap items-center gap-1">
          <li>
            <Link href="/auctions" className="hover:text-ink hover:underline">
              {tc("nav.auctions")}
            </Link>
          </li>
          <li aria-hidden>
            <ChevronRight className="flip-rtl h-4 w-4" />
          </li>
          <li aria-current="page" className="line-clamp-1 text-ink-2">
            {title}
          </li>
        </ol>
      </nav>

      {!c.isActive && (
        <div className="mb-4" dir={textDir}>
          <Alert tone="warn">{t("inactiveNotice")}</Alert>
        </div>
      )}

      {/* Column placement is left-to-right in both languages (bid panel left, details right); text inside keeps the language's own direction. */}
      <div className="grid gap-8 lg:grid-cols-[24rem_1fr]" dir="ltr">
        <div className="min-w-0 space-y-6" dir={textDir}>
          <header className="space-y-2">
            <div className="flex flex-wrap items-center gap-2">
              <PhaseBadge phase={c.phase} />
              <span className="tabular rounded-sm border border-line-strong px-1.5 py-0.5 text-xs font-medium text-ink-2">
                {t("lot", { ref: c.refNumber })}
              </span>
              {c.category && <span className="text-sm text-muted">{pick(locale, c.category.titleEn, c.category.titleAr)}</span>}
            </div>
            <h1 className="text-2xl font-semibold sm:text-3xl">{title}</h1>
            {pick(locale, c.shortDescEn, c.shortDescAr) && <p className="max-w-[70ch] text-ink-2">{pick(locale, c.shortDescEn, c.shortDescAr)}</p>}
          </header>

          <Gallery fileIds={photos} title={title} />

          <div className="flex flex-wrap gap-2">
            <FavoriteButton auctionId={c.id} initial={a.viewer.isFavorite} variant="full" />
            <WatchButton auctionId={c.id} initial={a.viewer.isWatched} />
            {a.videoUrl && (
              <a href={a.videoUrl} target="_blank" rel="noopener noreferrer" className="inline-flex h-10 items-center gap-2 rounded-md border border-line-strong bg-surface px-3 text-sm font-medium hover:bg-sunken">
                <PlayCircle className="h-4 w-4" aria-hidden /> {t("video")}
              </a>
            )}
          </div>

          <dl className="grid gap-4 rounded-lg border border-line bg-surface p-4 text-sm sm:grid-cols-3">
            <div>
              <dt className="flex items-center gap-1.5 text-muted">
                <CalendarClock className="h-4 w-4" aria-hidden /> {t("starts")}
              </dt>
              <dd className="mt-0.5 font-medium">{dateTime(c.startsAtUtc, locale)}</dd>
            </div>
            <div>
              <dt className="flex items-center gap-1.5 text-muted">
                <CalendarClock className="h-4 w-4" aria-hidden /> {t("ends")}
              </dt>
              <dd className="mt-0.5 font-medium">{c.endsAtUtc ? dateTime(c.endsAtUtc, locale) : t("noEnd")}</dd>
            </div>
            <div>
              <dt className="text-muted">{t("country")}</dt>
              <dd className="mt-0.5 font-medium">{pick(locale, a.countryNameEn, a.countryNameAr)}</dd>
              {days && <dd className="text-xs text-muted">{t("duration", { days })}</dd>}
            </div>
          </dl>

          <Tabs items={tabs} />
        </div>

        <aside className="space-y-4 lg:order-first lg:sticky lg:top-24 lg:self-start" dir={textDir}>
          <BidPanel
            auctionId={c.id}
            refNumber={c.refNumber}
            phase={c.phase}
            startsAtUtc={c.startsAtUtc}
            endsAtUtc={c.endsAtUtc}
            currentPrice={c.currentPrice}
            minimumNextBid={a.minimumNextBid}
            increment={c.bidIncrement}
            insurance={c.insuranceAmount}
            bidCount={c.bidCount}
            serverNow={serverNow}
            viewer={{ isOwner: a.viewer.isOwner, walletBalance: a.viewer.walletBalance ?? null, isLeading: a.viewer.isLeading }}
          />
          {a.publisher && (
            <section className="relative flex items-center gap-3 rounded-lg border border-line bg-surface p-4">
              <div className="h-12 w-12 shrink-0 overflow-hidden rounded-md bg-sunken">
                {a.publisher.logoFileId ? (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img src={fileUrl(a.publisher.logoFileId)} alt="" className="h-full w-full object-cover" />
                ) : (
                  <Store className="m-3 h-6 w-6 text-muted" aria-hidden />
                )}
              </div>
              <div className="min-w-0">
                <p className="text-xs text-muted">{t("by")}</p>
                <Link href={`/stores/${a.publisher.id}`} className="font-semibold after:absolute after:inset-0 hover:underline">
                  <bdi>{a.publisher.name}</bdi>
                </Link>
                <p className="tabular text-xs text-muted">{t("storeLots", { count: a.publisher.auctionCount })}</p>
              </div>
            </section>
          )}
        </aside>
      </div>

      {a.related.length > 0 && (
        <section className="mt-14">
          <h2 className="mb-5 text-xl font-semibold">{t("related")}</h2>
          <AuctionGrid items={a.related.slice(0, 3)} serverNow={serverNow} />
        </section>
      )}
    </div>
  );
}
