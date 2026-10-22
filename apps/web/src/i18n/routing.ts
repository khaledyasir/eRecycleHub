import { defineRouting } from "next-intl/routing";

export const routing = defineRouting({
  locales: ["en", "ar"],
  defaultLocale: "ar",
  // Arabic is the site language: do not switch visitors to English from their browser language or an old language cookie.
  // The language switcher still works because it links to the explicit /en and /ar paths.
  localeDetection: false,
  localeCookie: { name: "erh_locale", maxAge: 60 * 60 * 24 * 365 },
});

export type Locale = (typeof routing.locales)[number];
export const isRtl = (locale: string) => locale === "ar";
