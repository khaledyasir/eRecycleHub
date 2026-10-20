import { ArrowLeft, Store } from "lucide-react";
import type { ReactNode } from "react";
import { Link } from "@/i18n/navigation";
import { PortalNav, type PortalNavGroup } from "./portal-nav";

/**
 * Customer account and seller portal: a side menu next to the content on large screens (with who is signed in), and a scrolling
 * row of pills above the content on phones and tablets. Both sit inside the normal site header and footer.
 * `cta` is a highlighted action kept out of the menu list (for example "Become a seller").
 */
export function PortalShell({ title, subtitle, groups, navLabel, cta, children }: {
  title: string; subtitle?: string; groups: PortalNavGroup[]; navLabel: string; cta?: { href: string; label: string; hint?: string }; children: ReactNode;
}) {
  const initial = (subtitle ?? title).trim().charAt(0).toUpperCase();
  return (
    <div className="container-page py-8 lg:grid lg:grid-cols-[17rem_minmax(0,1fr)] lg:items-start lg:gap-8 lg:py-10">
      <aside className="hidden lg:sticky lg:top-28 lg:block">
        <div className="rounded-2xl border border-line bg-surface p-4 shadow-sm">
          <div className="soft-hero mb-4 flex items-center gap-3 rounded-xl border border-patina-100 p-3">
            <span className="grid h-11 w-11 shrink-0 place-items-center rounded-full bg-patina text-lg font-black text-white" aria-hidden>
              {initial}
            </span>
            <div className="min-w-0">
              <p className="text-xs font-bold text-patina">{title}</p>
              {subtitle && <p className="truncate text-sm font-black">{subtitle}</p>}
            </div>
          </div>
          <PortalNav groups={groups} label={navLabel} variant="side" />
        </div>
        {cta && (
          <Link href={cta.href} className="mt-4 flex items-center gap-3 rounded-2xl bg-orange p-4 text-white shadow-lg shadow-orange/25 transition hover:-translate-y-0.5 hover:brightness-105 motion-reduce:transform-none">
            <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-white/20">
              <Store className="h-5 w-5" aria-hidden />
            </span>
            <span className="min-w-0 flex-1">
              <span className="block font-black">{cta.label}</span>
              {cta.hint && <span className="block text-xs text-white/85">{cta.hint}</span>}
            </span>
            <ArrowLeft className="h-4 w-4 shrink-0 ltr:rotate-180" aria-hidden />
          </Link>
        )}
      </aside>
      <div className="min-w-0">
        <p className="sr-only lg:hidden">
          {title}
          {subtitle ? ` — ${subtitle}` : ""}
        </p>
        <div className="lg:hidden">
          <PortalNav groups={groups} label={navLabel} />
          {cta && (
            <Link href={cta.href} className="-mt-2 mb-5 flex items-center justify-center gap-2 rounded-xl bg-orange px-4 py-3 text-sm font-black text-white">
              <Store className="h-4 w-4" aria-hidden /> {cta.label}
            </Link>
          )}
        </div>
        {children}
      </div>
    </div>
  );
}
