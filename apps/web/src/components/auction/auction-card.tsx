import { Gavel, ImageOff } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Link } from "@/i18n/navigation";
import type { AuctionCard as Card, AuctionPhase } from "@/lib/api/types";
import { cn, fileUrl, money, pick } from "@/lib/format";
import { Badge, type Tone } from "@/components/ui/display";
import { Countdown } from "./countdown";
import { FavoriteButton } from "./toggles";

export const phaseTone: Record<AuctionPhase, Tone> = { Live: "live", Upcoming: "upcoming", Ended: "ended" };

export function PhaseBadge({ phase }: { phase: AuctionPhase }) {
  const t = useTranslations("common.phase");
  return (
    <Badge tone={phaseTone[phase]}>
      {phase === "Live" && <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-hivis-ink motion-reduce:animate-none" aria-hidden />}
      {t(phase)}
    </Badge>
  );
}

/** A lot "ticket": photo with its stamped lot number, the current price, and a strip with the clock and bid count. */
export function AuctionCard({ a, serverNow, className }: { a: Card; serverNow: number; className?: string }) {
  const t = useTranslations("auction.card");
  const locale = useLocale();
  const title = pick(locale, a.titleEn, a.titleAr);
  return (
    <article className={cn("group relative flex flex-col overflow-hidden rounded-lg border border-line bg-surface transition-shadow hover:shadow-raise", className)}>
      <div className="relative aspect-[4/3] overflow-hidden bg-sunken">
        {a.mainImageFileId ? (
          // eslint-disable-next-line @next/next/no-img-element -- uploaded files are served by the API with long-lived caching
          <img src={fileUrl(a.mainImageFileId)} alt="" loading="lazy" decoding="async" className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.02] motion-reduce:transform-none" />
        ) : (
          <div className="flex h-full items-center justify-center text-muted">
            <ImageOff className="h-8 w-8" aria-hidden />
          </div>
        )}
        <div className="absolute start-2 top-2">
          <PhaseBadge phase={a.phase} />
        </div>
        <span className="tabular absolute bottom-2 start-2 rounded-sm border border-white/40 bg-ink/80 px-1.5 py-0.5 text-[11px] font-medium text-white" dir="ltr">
          {t("lot", { ref: a.refNumber })}
        </span>
      </div>
      <div className="absolute end-2 top-2 z-10">
        <FavoriteButton auctionId={a.id} initial={a.isFavorite} />
      </div>
      <div className="flex flex-1 flex-col gap-3 p-4">
        <div className="min-w-0">
          <h3 className="line-clamp-2 text-base font-semibold">
            <Link href={`/auctions/${a.id}`} className="after:absolute after:inset-0 focus-visible:outline-none">
              {title}
            </Link>
          </h3>
          <p className="mt-1 truncate text-sm text-muted">
            {a.publisher?.name ?? t("platform")}
            {a.category && ` · ${pick(locale, a.category.titleEn, a.category.titleAr)}`}
          </p>
        </div>
        <div className="mt-auto">
          <p className="text-xs text-muted">{a.bidCount > 0 ? t("currentBid") : t("startingBid")}</p>
          <p className="tabular text-xl font-semibold">{money(a.currentPrice, locale)}</p>
        </div>
      </div>
      <div className={cn("flex items-center justify-between gap-2 border-t px-4 py-2 text-sm", a.phase === "Live" ? "border-hivis bg-hivis-soft" : "border-line bg-ground")}>
        <span className="inline-flex items-center gap-1.5 text-ink-2">
          {a.phase === "Upcoming" ? (
            <>
              <span className="text-xs text-muted">{t("startsIn")}</span>
              <Countdown target={a.startsAtUtc} serverNow={serverNow} mode="start" />
            </>
          ) : a.phase === "Live" ? (
            <Countdown target={a.endsAtUtc} serverNow={serverNow} />
          ) : (
            <span className="text-muted">{t("closed")}</span>
          )}
        </span>
        <span className="inline-flex items-center gap-1 text-ink-2">
          <Gavel className="h-4 w-4" aria-hidden />
          <span className="tabular">{t("bids", { count: a.bidCount })}</span>
        </span>
      </div>
    </article>
  );
}

export function AuctionGrid({ items, serverNow, className }: { items: Card[]; serverNow: number; className?: string }) {
  return (
    <ul className={cn("grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4", className)}>
      {items.map((a) => (
        <li key={a.id} className="flex">
          <AuctionCard a={a} serverNow={serverNow} className="w-full" />
        </li>
      ))}
    </ul>
  );
}
