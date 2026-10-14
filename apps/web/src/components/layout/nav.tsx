"use client";

import { Dialog as D } from "radix-ui";
import { Menu as MenuIcon, X } from "lucide-react";
import { useTranslations } from "next-intl";
import { Suspense, useState } from "react";
import { Link, usePathname } from "@/i18n/navigation";
import { cn } from "@/lib/format";
import { CountrySwitcher, LocaleSwitcher } from "./switchers";

type NavLink = { href: string; label: string };

const isActive = (pathname: string, href: string) => pathname === href || pathname.startsWith(href + "/");

export function NavLinks({ links, label }: { links: NavLink[]; label: string }) {
  const pathname = usePathname();
  return (
    <nav aria-label={label} className="hidden md:block">
      <ul className="flex items-center gap-1">
        {links.map((l) => (
          <li key={l.href}>
            <Link
              href={l.href}
              aria-current={isActive(pathname, l.href) ? "page" : undefined}
              className={cn(
                "inline-flex h-10 items-center rounded-md px-3 text-sm font-medium text-ink-2 hover:bg-sunken hover:text-ink",
                isActive(pathname, l.href) && "text-ink underline decoration-hivis decoration-[3px] underline-offset-[10px]",
              )}
            >
              {l.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}

/** Off-canvas navigation for phones, including the country and language controls hidden from the compact header. */
export function MobileNav({ links, countries, countryId, signedIn }: { links: NavLink[]; countries: { id: number; nameEn: string; nameAr: string }[]; countryId?: number; signedIn: boolean }) {
  const t = useTranslations("common");
  const [open, setOpen] = useState(false);
  const pathname = usePathname();
  const extra = [
    { href: "/", label: t("nav.home") },
    ...links,
    { href: "/benefits", label: t("nav.benefits") },
    { href: "/faq", label: t("nav.faq") },
    { href: "/contact", label: t("nav.contact") },
    ...(signedIn ? [] : [{ href: "/login", label: t("user.signIn") }]),
  ];
  return (
    <D.Root open={open} onOpenChange={setOpen}>
      <D.Trigger className="inline-flex h-10 w-10 items-center justify-center rounded-md hover:bg-sunken md:hidden" aria-label={t("nav.menu")}>
        <MenuIcon className="h-5 w-5" />
      </D.Trigger>
      <D.Portal>
        <D.Overlay className="fixed inset-0 z-50 bg-ink/50" />
        <D.Content className="fixed inset-y-0 start-0 z-50 flex w-[min(20rem,85vw)] flex-col bg-surface shadow-pop focus:outline-none">
          <div className="flex h-16 items-center justify-between border-b border-line px-4">
            <D.Title className="font-semibold">{t("nav.menu")}</D.Title>
            <D.Description className="sr-only">{t("nav.main")}</D.Description>
            <D.Close className="rounded p-2 hover:bg-sunken" aria-label={t("close")}>
              <X className="h-5 w-5" />
            </D.Close>
          </div>
          <nav className="flex-1 overflow-y-auto p-2" aria-label={t("nav.main")}>
            {extra.map((l) => (
              <Link
                key={l.href}
                href={l.href}
                onClick={() => setOpen(false)}
                aria-current={pathname === l.href ? "page" : undefined}
                className="flex h-11 items-center rounded-md px-3 font-medium hover:bg-sunken aria-[current=page]:bg-patina-50 aria-[current=page]:text-patina-700"
              >
                {l.label}
              </Link>
            ))}
          </nav>
          <div className="flex flex-col gap-2 border-t border-line p-4">
            <CountrySwitcher countries={countries} selected={countryId} />
            <Suspense>
              <LocaleSwitcher className="self-start" />
            </Suspense>
          </div>
        </D.Content>
      </D.Portal>
    </D.Root>
  );
}
