import type { Metadata } from "next";
import { CalendarDays, ChevronLeft, ChevronRight } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import type { Schema } from "@/lib/api/types";
import { cn, fromLocalInput, pick, toLocalInput } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.calendar"))("title") };
}

type Item = Schema<"CalendarItem">;
type Event = { item: Item; kind: "starts" | "ends" };

const pad = (n: number) => String(n).padStart(2, "0");
/** Calendar day ("YYYY-MM-DD") of an instant in the display time zone. */
const dayOf = (iso: string) => toLocalInput(iso).slice(0, 10);

export default async function CalendarPage({ searchParams }: { searchParams: Promise<{ month?: string }> }) {
  const sp = await searchParams;
  const [t, locale, client] = await Promise.all([getTranslations("account.calendar"), getLocale(), serverApi()]);

  const today = toLocalInput(new Date().toISOString()).slice(0, 7);
  const month = /^\d{4}-\d{2}$/.test(sp.month ?? "") ? sp.month! : today;
  const [y, m] = month.split("-").map(Number);
  const next = m === 12 ? `${y + 1}-01` : `${y}-${pad(m + 1)}`;
  const prev = m === 1 ? `${y - 1}-12` : `${y}-${pad(m - 1)}`;
  const fromUtc = fromLocalInput(`${month}-01T00:00`);
  const toUtc = fromLocalInput(`${next}-01T00:00`);
  const items = await load(client.GET("/api/auctions/calendar", { params: { query: { from: fromUtc, to: toUtc } } }));

  const byDay = new Map<string, Event[]>();
  for (const item of items) {
    const add = (iso: string | null | undefined, kind: Event["kind"]) => {
      if (!iso) return;
      const d = dayOf(iso);
      if (!d.startsWith(month)) return;
      byDay.set(d, [...(byDay.get(d) ?? []), { item, kind }]);
    };
    add(item.startsAtUtc, "starts");
    add(item.endsAtUtc, "ends");
  }

  const daysInMonth = new Date(Date.UTC(y, m, 0)).getUTCDate();
  const weekStart = locale === "ar" ? 6 : 1; // Saturday for Arabic, Monday for English
  const firstDow = new Date(Date.UTC(y, m - 1, 1)).getUTCDay();
  const lead = (firstDow - weekStart + 7) % 7;
  const cells: (number | null)[] = [...Array(lead).fill(null), ...Array.from({ length: daysInMonth }, (_, i) => i + 1)];
  while (cells.length % 7) cells.push(null);
  const weeks = Array.from({ length: cells.length / 7 }, (_, i) => cells.slice(i * 7, i * 7 + 7));
  const tag = locale === "ar" ? "ar-JO-u-nu-latn" : "en-GB";
  const weekday = (i: number) => new Intl.DateTimeFormat(tag, { weekday: "short", timeZone: "UTC" }).format(new Date(Date.UTC(2024, 0, 7 + ((weekStart + i) % 7))));
  const title = new Intl.DateTimeFormat(tag, { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(Date.UTC(y, m - 1, 1)));
  const todayKey = toLocalInput(new Date().toISOString()).slice(0, 10);

  const EventLink = ({ e }: { e: Event }) => (
    <Link
      href={`/auctions/${e.item.id}`}
      className={cn("block truncate rounded-sm px-1.5 py-0.5 text-xs", e.kind === "starts" ? "bg-info-soft text-info" : "bg-hivis-soft text-hivis-ink")}
      title={`${t(e.kind)}: ${pick(locale, e.item.titleEn, e.item.titleAr)}`}
    >
      <span className="font-semibold">{t(e.kind)}</span> {pick(locale, e.item.titleEn, e.item.titleAr)}
    </Link>
  );

  const nav = (
    <div className="flex items-center gap-1">
      <Link href={`/account/calendar?month=${prev}`} className="inline-flex h-9 w-9 items-center justify-center rounded-md border border-line-strong bg-surface hover:bg-sunken" aria-label={t("previous")}>
        <ChevronLeft className="flip-rtl h-4 w-4" />
      </Link>
      <Link href="/account/calendar" className="inline-flex h-9 items-center rounded-md border border-line-strong bg-surface px-3 text-sm hover:bg-sunken">
        {t("today")}
      </Link>
      <Link href={`/account/calendar?month=${next}`} className="inline-flex h-9 w-9 items-center justify-center rounded-md border border-line-strong bg-surface hover:bg-sunken" aria-label={t("next")}>
        <ChevronRight className="flip-rtl h-4 w-4" />
      </Link>
    </div>
  );

  const days = [...byDay.entries()].sort(([a], [b]) => a.localeCompare(b));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} actions={nav} />
      <h2 className="mb-3 text-lg font-semibold">{title}</h2>

      {/* Month grid (tablet and up) */}
      <table className="hidden w-full table-fixed border-collapse overflow-hidden rounded-lg border border-line bg-surface text-sm md:table">
        <thead>
          <tr>
            {Array.from({ length: 7 }, (_, i) => (
              <th key={i} scope="col" className="border-b border-line bg-sunken/60 py-2 text-xs font-semibold text-ink-2">
                {weekday(i)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {weeks.map((w, wi) => (
            <tr key={wi}>
              {w.map((d, di) => {
                const key = d ? `${month}-${pad(d)}` : "";
                const events = d ? byDay.get(key) ?? [] : [];
                return (
                  <td key={di} className={cn("h-28 border border-line p-1 align-top", !d && "bg-ground")}>
                    {d && (
                      <>
                        <span className={cn("tabular inline-flex h-6 w-6 items-center justify-center rounded-full text-xs", key === todayKey && "bg-patina font-semibold text-white")}>{d}</span>
                        <div className="mt-1 space-y-0.5">
                          {events.slice(0, 3).map((e, i) => (
                            <EventLink key={i} e={e} />
                          ))}
                          {events.length > 3 && <p className="px-1.5 text-xs text-muted">{t("more", { count: events.length - 3 })}</p>}
                        </div>
                      </>
                    )}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>

      {/* Agenda list (phones) */}
      <div className="md:hidden">
        {days.length === 0 ? null : (
          <ol className="space-y-3">
            {days.map(([d, events]) => (
              <li key={d} className="rounded-lg border border-line bg-surface p-3">
                <p className="mb-2 text-sm font-semibold">{new Intl.DateTimeFormat(tag, { weekday: "long", day: "numeric", month: "long", timeZone: "UTC" }).format(new Date(`${d}T00:00:00Z`))}</p>
                <div className="space-y-1">
                  {events.map((e, i) => (
                    <EventLink key={i} e={e} />
                  ))}
                </div>
              </li>
            ))}
          </ol>
        )}
      </div>
      {days.length === 0 && <EmptyState className="mt-4" icon={<CalendarDays className="h-8 w-8" />} title={t("empty")} />}
    </>
  );
}
