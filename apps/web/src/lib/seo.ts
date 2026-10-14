import type { Metadata } from "next";
import { routing } from "@/i18n/routing";

export const siteUrl = () => (process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000").replace(/\/$/, "");

/** Canonical URL for this locale plus hreflang alternates for every locale (path without locale prefix, e.g. "/auctions"). */
export function alternates(locale: string, path: string): Metadata["alternates"] {
  const clean = path === "/" ? "" : path;
  return {
    canonical: `/${locale}${clean}`,
    languages: Object.fromEntries([...routing.locales.map((l) => [l, `/${l}${clean}`]), ["x-default", `/${routing.defaultLocale}${clean}`]]),
  };
}

/** Plain text excerpt from sanitised HTML for meta descriptions. */
export const excerpt = (html: string | null | undefined, max = 160) => {
  const text = (html ?? "").replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim();
  return text.length > max ? text.slice(0, max - 1).trimEnd() + "…" : text;
};
