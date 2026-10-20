import type { ReactNode } from "react";
import { PortalNav, type PortalNavGroup } from "./portal-nav";

/**
 * Customer account and seller portal: a side menu next to the content on large screens (with who is signed in), and a scrolling
 * row of pills above the content on phones and tablets. Both sit inside the normal site header and footer.
 */
export function PortalShell({ title, subtitle, groups, navLabel, children }: { title: string; subtitle?: string; groups: PortalNavGroup[]; navLabel: string; children: ReactNode }) {
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
      </aside>
      <div className="min-w-0">
        <p className="sr-only lg:hidden">
          {title}
          {subtitle ? ` — ${subtitle}` : ""}
        </p>
        <div className="lg:hidden">
          <PortalNav groups={groups} label={navLabel} />
        </div>
        {children}
      </div>
    </div>
  );
}
