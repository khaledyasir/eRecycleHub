import type { Metadata, Viewport } from "next";
import { IBM_Plex_Sans, IBM_Plex_Sans_Arabic } from "next/font/google";
import { notFound } from "next/navigation";
import { hasLocale, NextIntlClientProvider } from "next-intl";
import { getTranslations, setRequestLocale } from "next-intl/server";
import type { ReactNode } from "react";
import { Providers } from "@/components/providers";
import { isRtl, routing } from "@/i18n/routing";
import { getSession } from "@/lib/api/server";
import { siteUrl } from "@/lib/seo";
import "../globals.css";

const plex = IBM_Plex_Sans({ subsets: ["latin"], weight: ["400", "500", "600", "700"], variable: "--font-plex", display: "swap" });
const plexArabic = IBM_Plex_Sans_Arabic({ subsets: ["arabic"], weight: ["400", "500", "600", "700"], variable: "--font-plex-arabic", display: "swap" });

export const viewport: Viewport = { themeColor: "#0e6b57", width: "device-width", initialScale: 1 };

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: "common" });
  return {
    metadataBase: new URL(siteUrl()),
    title: { default: `${t("brand")} — ${t("tagline")}`, template: `%s · ${t("brand")}` },
    description: t("footer.about"),
    applicationName: t("brand"),
    openGraph: { siteName: t("brand"), locale: locale === "ar" ? "ar_JO" : "en_JO", type: "website" },
  };
}

export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export default async function LocaleLayout({ children, params }: { children: ReactNode; params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  setRequestLocale(locale);
  const user = await getSession();
  const dir = isRtl(locale) ? "rtl" : "ltr";
  const t = await getTranslations("common");
  return (
    <html lang={locale} dir={dir} className={`${plex.variable} ${plexArabic.variable}`}>
      <body className="min-h-dvh antialiased">
        <a href="#main" className="sr-only z-50 rounded bg-ink px-3 py-2 text-white focus:not-sr-only focus:fixed focus:start-3 focus:top-3">
          {t("skipToContent")}
        </a>
        <NextIntlClientProvider>
          <Providers user={user} dir={dir}>
            {children}
          </Providers>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
