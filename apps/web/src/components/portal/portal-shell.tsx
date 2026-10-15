import type { ReactNode } from "react";
import { PortalNav, type PortalNavGroup } from "./portal-nav";

/** Customer account and seller portal: a tab bar of pages above the content, inside the normal site header and footer. */
export function PortalShell({ title, subtitle, groups, navLabel, children }: { title: string; subtitle?: string; groups: PortalNavGroup[]; navLabel: string; children: ReactNode }) {
  return (
    <div className="container-page py-9">
      <p className="sr-only">
        {title}
        {subtitle ? ` — ${subtitle}` : ""}
      </p>
      <PortalNav groups={groups} label={navLabel} />
      <div className="min-w-0">{children}</div>
    </div>
  );
}
