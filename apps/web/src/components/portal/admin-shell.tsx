"use client";

import { Dialog as D } from "radix-ui";
import { ArrowLeft, Menu as MenuIcon, X } from "lucide-react";
import { useTranslations } from "next-intl";
import { Suspense, useState, type ReactNode } from "react";
import { Logo } from "@/components/layout/logo";
import { LocaleSwitcher } from "@/components/layout/switchers";
import { UserMenu } from "@/components/layout/user-menu";
import { Link, usePathname } from "@/i18n/navigation";
import { isActive, PortalNav, type PortalNavGroup } from "./portal-nav";

/**
 * Admin console: its own sidebar and top bar (no public site header or footer), as in the approved mockup.
 * Below `lg` the sidebar becomes an off-canvas drawer.
 */
export function AdminShell({ title, subtitle, groups, navLabel, children }: { title: string; subtitle?: string; groups: PortalNavGroup[]; navLabel: string; children: ReactNode }) {
  const t = useTranslations("admin.nav");
  const pathname = usePathname();
  const [open, setOpen] = useState(false);
  const current = groups.flatMap((g) => g.items).filter((i) => isActive(pathname, i)).sort((a, b) => b.href.length - a.href.length)[0];
  const sidebar = (onNavigate?: () => void) => (
    <>
      <Link href="/admin" className="inline-block" onClick={onNavigate}>
        <Logo />
      </Link>
      <p className="mb-6 mt-2 text-xs font-bold text-muted">{title}</p>
      <PortalNav groups={groups} label={navLabel} variant="sidebar" onNavigate={onNavigate} />
      <Link href="/" className="mt-4 flex items-center gap-2 border-t border-line p-3 text-sm font-bold text-muted hover:text-patina">
        <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden />
        {t("backToSite")}
      </Link>
    </>
  );
  return (
    <div className="min-h-dvh bg-ground lg:flex">
      <aside className="sticky top-0 hidden h-dvh w-72 shrink-0 overflow-y-auto border-e border-line bg-surface p-5 lg:block">{sidebar()}</aside>
      <div className="min-w-0 flex-1">
        <header className="sticky top-0 z-30 border-b border-line bg-surface">
          <div className="mx-auto flex h-20 max-w-[1600px] items-center justify-between gap-3 px-4 sm:px-8">
            <div className="flex items-center gap-3">
              <D.Root open={open} onOpenChange={setOpen}>
                <D.Trigger className="rounded-md bg-sunken p-2.5 lg:hidden" aria-label={t("menu")}>
                  <MenuIcon className="h-5 w-5" />
                </D.Trigger>
                <D.Portal>
                  <D.Overlay className="fixed inset-0 z-50 bg-ink/50" />
                  <D.Content className="fixed inset-y-0 start-0 z-50 w-[min(20rem,88vw)] overflow-y-auto bg-surface p-5 shadow-pop focus:outline-none">
                    <D.Title className="sr-only">{title}</D.Title>
                    <D.Description className="sr-only">{navLabel}</D.Description>
                    <D.Close className="absolute end-3 top-3 rounded p-2 hover:bg-sunken" aria-label={t("close")}>
                      <X className="h-5 w-5" />
                    </D.Close>
                    {sidebar(() => setOpen(false))}
                  </D.Content>
                </D.Portal>
              </D.Root>
              <div>
                <p className="text-xs text-muted">{title}</p>
                <p className="text-lg font-black sm:text-xl">{current?.label ?? title}</p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <span className="hidden max-w-48 truncate text-xs font-bold text-muted sm:inline">{subtitle}</span>
              <Suspense>
                <LocaleSwitcher />
              </Suspense>
              <UserMenu />
            </div>
          </div>
        </header>
        <main id="main" className="mx-auto max-w-[1600px] px-4 py-8 sm:px-8">
          <div className="brand-gradient mb-7 h-1.5 rounded-full" aria-hidden />
          {children}
        </main>
      </div>
    </div>
  );
}
