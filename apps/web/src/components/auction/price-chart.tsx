"use client";

import { useLocale, useTranslations } from "next-intl";
import { useId } from "react";
import { money } from "@/lib/format";

const W = 320;
const H = 80;
const PAD = 8;

/** Geometry of the sparkline: evenly spaced points joined by horizontal-tangent curves, plus the closed area under them. */
export function sparkline(points: number[], w = W, h = H, pad = PAD) {
  if (points.length < 2) return null;
  const low = Math.min(...points);
  const span = Math.max(...points) - low || 1;
  const r = (n: number) => Math.round(n * 100) / 100;
  const xy = points.map((p, i) => ({ x: r(pad + ((w - 2 * pad) * i) / (points.length - 1)), y: r(pad + (h - 2 * pad) * (1 - (p - low) / span)) }));
  const line = xy.reduce((d, b, i) => {
    if (i === 0) return `M${b.x},${b.y}`;
    const a = xy[i - 1];
    const mid = r((a.x + b.x) / 2);
    return `${d} C${mid},${a.y} ${mid},${b.y} ${b.x},${b.y}`;
  }, "");
  const end = xy[xy.length - 1];
  return { line, area: `${line} L${end.x},${h} L${xy[0].x},${h} Z`, end };
}

/** How the price rose: the start price, then each bid oldest first. Hidden until there is a bid. */
export function PriceChart({ startPrice, bids }: { startPrice: number; bids: number[] }) {
  const t = useTranslations("auction.detail");
  const locale = useLocale();
  const gradient = useId();
  const points = [...(startPrice > 0 ? [startPrice] : []), ...bids];
  const s = bids.length ? sparkline(points) : null;
  if (!s) return null;
  const from = money(points[0], locale);
  const to = money(points[points.length - 1], locale);

  return (
    // Time runs left to right in both languages.
    <figure dir="ltr" className="rounded-lg border border-line bg-surface p-3 text-patina">
      <div className="relative h-20">
        <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className="h-full w-full" role="img" aria-label={t("historyChart", { from, to, count: bids.length })}>
          <defs>
            <linearGradient id={gradient} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stopColor="currentColor" stopOpacity="0.25" />
              <stop offset="1" stopColor="currentColor" stopOpacity="0" />
            </linearGradient>
          </defs>
          <path d={s.area} fill={`url(#${gradient})`} />
          <path d={s.line} fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
        </svg>
        {/* The svg stretches to the box, so the end dot is an element of its own to stay round. */}
        <span
          aria-hidden
          className="absolute h-2.5 w-2.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-patina ring-4 ring-patina/20"
          style={{ left: `${(s.end.x / W) * 100}%`, top: `${(s.end.y / H) * 100}%` }}
        />
      </div>
      <figcaption aria-hidden className="tabular mt-1 flex justify-between gap-3 text-xs text-muted">
        <span>{from}</span>
        <span className="font-semibold text-patina">{to}</span>
      </figcaption>
    </figure>
  );
}
