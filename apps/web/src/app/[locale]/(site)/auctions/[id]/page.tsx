import { CalendarClock, ChevronRight, FileText, PlayCircle, Star, Store } from "lucide-react";
import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getLocale, getTranslations } from "next-intl/server";
import { AuctionGrid, PhaseBadge } from "@/components/auction/auction-card";
import { BidHistory } from "@/components/auction/bid-history";
import { BidPanel } from "@/components/auction/bid-panel";
import { Gallery } from "@/components/auction/gallery";
import { ReviewForm } from "@/components/auction/review-form";
import { FavoriteButton, WatchButton } from "@/components/auction/toggles";
import { RichHtml } from "@/components/rich-html";
import { Alert } from "@/components/ui/display";
import { Tabs } from "@/components/ui/controls";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { CURRENCY, date, dateTime, fileUrl, nowMs, pick } from "@/lib/format";
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
  const [a, t, tc, tr, locale, client] = await Promise.all([getAuction(id), getTranslations("auction.detail"), getTranslations("common"), getTranslations("auction.reviews"), getLocale(), serverApi()]);
  const reviews = await load(client.GET("/api/auctions/{id}/reviews", { params: { path: { id: a.card.id } } }));
  const serverNow = nowMs();
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
    ...(a.reviews.count > 0 ? { aggregateRating: { "@type": "AggregateRating", ratingValue: a.reviews.average, reviewCount: a.reviews.count } } : {}),
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
    { value: "history", label: t("history"), content: <BidHistory auctionId={c.id} live={c.phase === "Live"} /> },
    {
      value: "reviews",
      label: `${t("reviews")} (${a.reviews.count})`,
      content: (
        <div className="space-y-5">
          {a.reviews.count > 0 && <p className="font-medium">{tr("average", { rating: a.reviews.average, count: a.reviews.count })}</p>}
          {reviews.items.length === 0 ? (
            <p className="text-sm text-muted">{tr("empty")}</p>
          ) : (
            <ul className="space-y-3">
              {reviews.items.map((r) => (
                <li key={r.id} className="rounded-lg border border-line bg-surface p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-medium">{r.name}</span>
                    <span className="text-xs text-muted">{date(r.createdAtUtc, locale)}</span>
                  </div>
                  <p className="mt-1 flex gap-0.5" aria-label={tr("stars", { n: r.rating })}>
                    {[1, 2, 3, 4, 5].map((n) => (
                      <Star key={n} className={n <= r.rating ? "h-4 w-4 fill-hivis text-hivis" : "h-4 w-4 text-line-strong"} aria-hidden />
                    ))}
                  </p>
                  {r.comment && <p className="mt-2 text-sm text-ink-2">{r.comment}</p>}
                </li>
              ))}
            </ul>
          )}
          <ReviewForm auctionId={c.id} hasReviewed={a.viewer.hasReviewed} />
        </div>
      ),
    },
  ];

  return (
    <div className="container-page py-6">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd).replace(/</g, "\\u003c") }} />
      <nav aria-label="breadcrumb" className="mb-4 text-sm text-muted">
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
        <div className="mb-4">
          <Alert tone="warn">{t("inactiveNotice")}</Alert>
        </div>
      )}

      <div className="grid gap-8 lg:grid-cols-[1fr_24rem]">
        <div className="min-w-0 space-y-6">
          <header className="space-y-2">
            <div className="flex flex-wrap items-center gap-2">
              <PhaseBadge phase={c.phase} />
              <span className="tabular rounded-sm border border-line-strong px-1.5 py-0.5 text-xs font-medium text-ink-2" dir="ltr">
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

        <aside className="space-y-4 lg:sticky lg:top-24 lg:self-start">
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
                  {a.publisher.name}
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
          <AuctionGrid items={a.related.slice(0, 4)} serverNow={serverNow} />
        </section>
      )}
    </div>
  );
}
