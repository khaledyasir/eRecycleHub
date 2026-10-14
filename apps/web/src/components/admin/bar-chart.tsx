import { getLocale, getTranslations } from "next-intl/server";
import { number } from "@/lib/format";

/** 30-day bids chart: plain SVG bars from real data, with a text summary and a hidden data table for screen readers. */
export async function BidsChart({ points }: { points: { day: string; bids: number }[] }) {
  const [t, locale] = await Promise.all([getTranslations("admin.dashboard"), getLocale()]);
  const max = Math.max(0, ...points.map((p) => p.bids));
  const total = points.reduce((s, p) => s + p.bids, 0);
  const tag = locale === "ar" ? "ar-JO-u-nu-latn" : "en-GB";
  const fmt = (d: string) => new Intl.DateTimeFormat(tag, { day: "numeric", month: "short", timeZone: "UTC" }).format(new Date(`${d}T00:00:00Z`));
  const busiest = points.reduce((a, b) => (b.bids > a.bids ? b : a), points[0]);
  const W = 600, H = 160, gap = 4, bw = (W - gap * (points.length - 1)) / Math.max(1, points.length);
  return (
    <figure>
      <figcaption className="mb-3 text-sm text-muted">
        {total === 0 ? t("chartNone") : t("chartSummary", { total, day: fmt(busiest.day), max: busiest.bids })}
      </figcaption>
      <svg viewBox={`0 0 ${W} ${H + 20}`} className="h-44 w-full" role="img" aria-label={t("chartTitle")} preserveAspectRatio="none" style={{ direction: "ltr" }}>
        <line x1={0} x2={W} y1={H} y2={H} stroke="var(--color-line)" />
        {points.map((p, i) => {
          const h = max ? Math.max(p.bids ? 3 : 0, (p.bids / max) * (H - 10)) : 0;
          return (
            <g key={p.day}>
              <rect x={i * (bw + gap)} y={H - h} width={bw} height={h} rx={2} fill={i === points.length - 1 ? "var(--color-hivis)" : "var(--color-patina)"}>
                <title>{`${fmt(p.day)}: ${number(p.bids, locale)}`}</title>
              </rect>
            </g>
          );
        })}
        <text x={0} y={H + 15} fontSize={11} fill="var(--color-muted)">{fmt(points[0].day)}</text>
        <text x={W} y={H + 15} fontSize={11} fill="var(--color-muted)" textAnchor="end">{fmt(points[points.length - 1].day)}</text>
      </svg>
      <table className="sr-only">
        <caption>{t("chartTitle")}</caption>
        <tbody>
          {points.map((p) => (
            <tr key={p.day}><th scope="row">{fmt(p.day)}</th><td>{p.bids}</td></tr>
          ))}
        </tbody>
      </table>
    </figure>
  );
}
