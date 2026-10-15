"use client";

import { Globe } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { useTransition } from "react";
import { usePathname, useRouter } from "@/i18n/navigation";
import { routing } from "@/i18n/routing";
import { cn } from "@/lib/format";

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
