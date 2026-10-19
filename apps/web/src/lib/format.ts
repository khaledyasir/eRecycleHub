import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export const cn = (...inputs: ClassValue[]) => twMerge(clsx(inputs));

/** URL of an uploaded file (served by the API through the same-origin proxy). */
export const fileUrl = (id: string | null | undefined) => (id ? `/api/files/${id}` : undefined);

export const CURRENCY = process.env.NEXT_PUBLIC_CURRENCY ?? "JOD";
export const DISPLAY_TZ = process.env.NEXT_PUBLIC_DISPLAY_TIMEZONE ?? "Asia/Amman";

/** Intl tag per UI locale. Latin digits in both languages so amounts read the same as what users type into inputs. */
const tag = (locale: string) => (locale === "ar" ? "ar-JO-u-nu-latn" : "en-JO");

/** Money with the currency, always two decimals (the schema's precision). */
export function money(amount: number, locale: string) {
  return new Intl.NumberFormat(tag(locale), { style: "currency", currency: CURRENCY, minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
}

export function number(value: number, locale: string) {
  return new Intl.NumberFormat(tag(locale)).format(value);
}

/**
 * Dates are rendered in one display time zone (default Asia/Amman) (named once in the footer note, not on every date), on the server and in the
 * browser alike — deterministic output (no hydration mismatch) and unambiguous for visitors in other zones.
 */
export function dateTime(iso: string | null | undefined, locale: string, withTime = true) {
  if (!iso) return "—";
  const opts: Intl.DateTimeFormatOptions = withTime
    ? { year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" }
    : { year: "numeric", month: "short", day: "numeric" };
  return new Intl.DateTimeFormat(tag(locale), { timeZone: DISPLAY_TZ, ...opts }).format(new Date(iso));
}

export const date = (iso: string | null | undefined, locale: string) => dateTime(iso, locale, false);

export function relativeTime(iso: string, locale: string, nowMs: number) {
  const diff = (new Date(iso).getTime() - nowMs) / 1000;
  const rtf = new Intl.RelativeTimeFormat(tag(locale), { numeric: "auto" });
  const abs = Math.abs(diff);
  if (abs < 60) return rtf.format(Math.round(diff), "second");
  if (abs < 3600) return rtf.format(Math.round(diff / 60), "minute");
  if (abs < 86400) return rtf.format(Math.round(diff / 3600), "hour");
  if (abs < 86400 * 30) return rtf.format(Math.round(diff / 86400), "day");
  if (abs < 86400 * 365) return rtf.format(Math.round(diff / (86400 * 30)), "month");
  return rtf.format(Math.round(diff / (86400 * 365)), "year");
}

/** Picks the field for the UI language, falling back to the other one. */
export function pick(locale: string, en: string | null | undefined, ar: string | null | undefined) {
  return (locale === "ar" ? ar || en : en || ar) ?? "";
}

/** Value for <input type="datetime-local"> in the display time zone, and back to an ISO UTC string. */
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return "";
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: DISPLAY_TZ, year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23" })
    .formatToParts(new Date(iso))
    .reduce<Record<string, string>>((acc, p) => ((acc[p.type] = p.value), acc), {});
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
}

export function fromLocalInput(value: string): string {
  // Interpret the wall-clock value in DISPLAY_TZ: find the UTC instant whose display-zone rendering equals it.
  const [d, t] = value.split("T");
  const [y, m, day] = d.split("-").map(Number);
  const [hh, mm] = t.split(":").map(Number);
  const guess = Date.UTC(y, m - 1, day, hh, mm);
  const offset = (ms: number) => {
    const local = toLocalInput(new Date(ms).toISOString());
    const [ld, lt] = local.split("T");
    const [ly, lm, lday] = ld.split("-").map(Number);
    const [lh, lmin] = lt.split(":").map(Number);
    return Date.UTC(ly, lm - 1, lday, lh, lmin) - ms;
  };
  return new Date(guess - offset(guess - offset(guess))).toISOString();
}

/** Server components read the clock once per request and pass it down (e.g. as serverNow) so countdowns start from the server's time. */
export const nowMs = () => Date.now();
