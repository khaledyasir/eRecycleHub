import { ChevronLeft, ChevronRight } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Link } from "@/i18n/navigation";
import { cn } from "@/lib/format";

/** Server-rendered pagination: plain links that keep the current query string (works without JavaScript, crawlable). */
export async function Pagination({ page, totalPages, pathname, query, param = "page" }: { page: number; totalPages: number; pathname: string; query: Record<string, string | string[] | undefined>; param?: string }) {
  if (totalPages <= 1) return null;
  const t = await getTranslations("common.pagination");
  const href = (p: number) => {
    const q = new URLSearchParams();
    for (const [k, v] of Object.entries(query)) for (const one of Array.isArray(v) ? v : v ? [v] : []) if (k !== param) q.append(k, one);
    if (p > 1) q.set(param, String(p));
    const s = q.toString();
    return s ? `${pathname}?${s}` : pathname;
  };
  const pages = Array.from(new Set([1, page - 1, page, page + 1, totalPages].filter((p) => p >= 1 && p <= totalPages))).sort((a, b) => a - b);
  const item = "inline-flex h-9 min-w-9 items-center justify-center rounded-md px-2 text-sm";
  return (
    <nav aria-label={t("label")} className="mt-8 flex flex-wrap items-center justify-center gap-1">
      {page > 1 ? (
        <Link href={href(page - 1)} className={cn(item, "hover:bg-sunken")} rel="prev">
          <ChevronLeft className="flip-rtl h-4 w-4" aria-hidden />
          <span className="sr-only">{t("previous")}</span>
        </Link>
      ) : null}
      {pages.map((p, i) => (
        <span key={p} className="contents">
          {i > 0 && p - pages[i - 1] > 1 && <span className="px-1 text-muted">…</span>}
          <Link href={href(p)} aria-current={p === page ? "page" : undefined} className={cn(item, "tabular", p === page ? "bg-ink text-white" : "hover:bg-sunken")}>
            {p}
          </Link>
        </span>
      ))}
      {page < totalPages ? (
        <Link href={href(page + 1)} className={cn(item, "hover:bg-sunken")} rel="next">
          <ChevronRight className="flip-rtl h-4 w-4" aria-hidden />
          <span className="sr-only">{t("next")}</span>
        </Link>
      ) : null}
    </nav>
  );
}
