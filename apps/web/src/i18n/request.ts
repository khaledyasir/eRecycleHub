import { hasLocale } from "next-intl";
import { getRequestConfig } from "next-intl/server";
import { routing } from "./routing";

/** Message catalogues are split per area so each part of the app owns its own file. */
export const namespaces = ["common", "errors", "public", "auction", "auth", "account", "seller", "admin", "cms"] as const;

type Messages = Record<string, unknown>;

// Static import paths (not a template string) so the bundler tracks every catalogue and reloads edits in development.
const catalogues: Record<string, Record<(typeof namespaces)[number], () => Promise<{ default: Messages }>>> = {
  en: {
    common: () => import("../messages/en/common.json"),
    errors: () => import("../messages/en/errors.json"),
    public: () => import("../messages/en/public.json"),
    auction: () => import("../messages/en/auction.json"),
    auth: () => import("../messages/en/auth.json"),
    account: () => import("../messages/en/account.json"),
    seller: () => import("../messages/en/seller.json"),
    admin: () => import("../messages/en/admin.json"),
    cms: () => import("../messages/en/cms.json"),
  },
  ar: {
    common: () => import("../messages/ar/common.json"),
    errors: () => import("../messages/ar/errors.json"),
    public: () => import("../messages/ar/public.json"),
    auction: () => import("../messages/ar/auction.json"),
    auth: () => import("../messages/ar/auth.json"),
    account: () => import("../messages/ar/account.json"),
    seller: () => import("../messages/ar/seller.json"),
    admin: () => import("../messages/ar/admin.json"),
    cms: () => import("../messages/ar/cms.json"),
  },
};

async function load(locale: string): Promise<Messages> {
  const parts = await Promise.all(namespaces.map(async (ns) => [ns, (await catalogues[locale][ns]()).default] as const));
  return Object.fromEntries(parts);
}

/** Administrator overrides (dashboard → Translations) are applied on top of the shipped catalogue. Cached for a minute. */
async function overrides(locale: string): Promise<Record<string, string>> {
  try {
    const res = await fetch(`${process.env.API_INTERNAL_URL ?? "http://localhost:5080"}/api/i18n/${locale}`, { next: { revalidate: 60, tags: ["i18n"] } });
    return res.ok ? await res.json() : {};
  } catch {
    // The API being down must not take the whole site's text with it; shipped messages still render.
    return {};
  }
}

function apply(messages: Messages, flat: Record<string, string>) {
  for (const [key, value] of Object.entries(flat)) {
    const path = key.split(".");
    let node = messages as Record<string, unknown>;
    for (let i = 0; i < path.length - 1; i++) {
      const next = node[path[i]];
      if (typeof next !== "object" || next === null) {
        node = {};
        break;
      }
      node = next as Record<string, unknown>;
    }
    const leaf = path[path.length - 1];
    if (typeof node[leaf] === "string") node[leaf] = value; // only existing keys can be overridden
  }
  return messages;
}

export default getRequestConfig(async ({ requestLocale }) => {
  const requested = await requestLocale;
  const locale = hasLocale(routing.locales, requested) ? requested : routing.defaultLocale;
  const [messages, flat] = await Promise.all([load(locale), overrides(locale)]);
  return { locale, messages: apply(structuredClone(messages), flat), timeZone: process.env.NEXT_PUBLIC_DISPLAY_TIMEZONE ?? "Asia/Amman" };
});
