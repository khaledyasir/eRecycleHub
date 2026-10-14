import type { ReactNode } from "react";
import { PortalNav, type PortalNavGroup } from "./portal-nav";

/**
 * Two-column workspace used by the customer account, the seller portal and the admin panel:
 * a navigation column (a scrollable row on small screens) and the page content.
 */
export function PortalShell({ title, subtitle, groups, navLabel, children }: { title: string; subtitle?: string; groups: PortalNavGroup[]; navLabel: string; children: ReactNode }) {
  return (
    <div className="container-page py-6 lg:py-8">
      <div className="lg:grid lg:grid-cols-[15rem_1fr] lg:gap-8">
        <aside className="lg:sticky lg:top-24 lg:self-start">
          <div className="mb-3 hidden px-3 lg:block">
            <p className="font-semibold">{title}</p>
            {subtitle && <p className="truncate text-sm text-muted">{subtitle}</p>}
          </div>
          <PortalNav groups={groups} label={navLabel} />
        </aside>
        <div className="min-w-0 pt-5 lg:pt-0">{children}</div>
      </div>
    </div>
  );
}
