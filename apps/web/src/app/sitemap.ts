import type { MetadataRoute } from "next";
import { routing } from "@/i18n/routing";
import { siteUrl } from "@/lib/seo";

export const dynamic = "force-dynamic";

const STATIC = ["/", "/auctions", "/stores", "/about", "/benefits", "/how-to-buy", "/how-to-sell", "/faq", "/support", "/contact", "/advertise", "/apply", "/clients", "/terms", "/privacy"];

/** Every public page in both languages, with hreflang alternates. Auctions and stores come from the API. */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const base = siteUrl();
  let ids: { auctions: number[]; stores: number[] } = { auctions: [], stores: [] };
  try {
    const res = await fetch(`${process.env.API_INTERNAL_URL ?? "http://localhost:5080"}/api/sitemap`, { cache: "no-store" });
    if (res.ok) ids = await res.json();
  } catch {
    // The static pages are still listed if the API is unreachable.
  }
  const paths = [...STATIC, ...ids.auctions.map((id) => `/auctions/${id}`), ...ids.stores.map((id) => `/stores/${id}`)];
  return paths.flatMap((path) => {
    const p = path === "/" ? "" : path;
    const languages = Object.fromEntries(routing.locales.map((l) => [l, `${base}/${l}${p}`]));
    return routing.locales.map((locale) => ({ url: `${base}/${locale}${p}`, alternates: { languages } }));
  });
}
