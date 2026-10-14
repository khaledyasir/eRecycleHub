"use client";

import { Globe, MapPin } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { useTransition } from "react";
import { usePathname, useRouter } from "@/i18n/navigation";
import { routing } from "@/i18n/routing";
import { cn } from "@/lib/format";

export const COUNTRY_COOKIE = "erh_country";

/** Visitor's country (legacy: per-visitor cookie). Filters auctions, stores, categories, announcements and contact details. */
export function CountrySwitcher({ countries, selected, className }: { countries: { id: number; nameEn: string; nameAr: string }[]; selected?: number; className?: string }) {
  const t = useTranslations("common");
  const locale = useLocale();
  const router = useRouter();
  const [pending, start] = useTransition();
  return (
    <label className={cn("inline-flex items-center gap-1.5 text-sm", className)}>
      <MapPin className="h-4 w-4 text-muted" aria-hidden />
      <span className="sr-only">{t("country")}</span>
      <select
        value={selected ?? 0}
        disabled={pending}
        onChange={(e) => {
          const id = Number(e.target.value);
          document.cookie = id > 0 ? `${COUNTRY_COOKIE}=${id}; path=/; max-age=${60 * 60 * 24 * 365}; samesite=lax` : `${COUNTRY_COOKIE}=; path=/; max-age=0; samesite=lax`;
          start(() => router.refresh());
        }}
        className="cursor-pointer rounded-md bg-transparent py-1 pe-1 text-sm hover:bg-black/5 focus:outline-none"
      >
        <option value={0}>{t("allCountries")}</option>
        {countries.map((c) => (
          <option key={c.id} value={c.id}>
            {locale === "ar" ? c.nameAr : c.nameEn}
          </option>
        ))}
      </select>
    </label>
  );
}

/** Switches the URL locale prefix and keeps path + query (forms keep their state on reload because the URL carries filters). */
export function LocaleSwitcher({ className }: { className?: string }) {
  const t = useTranslations("common");
  const locale = useLocale();
  const pathname = usePathname();
  const search = useSearchParams();
  const router = useRouter();
  const [pending, start] = useTransition();
  const other = routing.locales.find((l) => l !== locale)!;
  return (
    <button
      type="button"
      disabled={pending}
      lang={other}
      onClick={() => start(() => router.replace(`${pathname}${search.size ? `?${search}` : ""}`, { locale: other }))}
      className={cn("inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm hover:bg-black/5", className)}
      aria-label={`${t("language")}: ${t(`languageNames.${other}`)}`}
    >
      <Globe className="h-4 w-4" aria-hidden />
      {t(`languageNames.${other}`)}
    </button>
  );
}
