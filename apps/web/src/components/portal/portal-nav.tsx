"use client";

import {
  BarChart3, Bell, BookOpen, Building2, CalendarDays, CircleHelp, ClipboardList, Eye, FileText, Flag, Gavel, Globe2, Heart, Image as ImageIcon,
  KeyRound, Languages, LayoutDashboard, ListChecks, Mail, Megaphone, MessageSquare, Phone, ScrollText, Settings, Shapes, ShieldCheck, Sparkles,
  Star, Store, Trophy, UserRound, Users, Wallet,
} from "lucide-react";
import { Link, usePathname } from "@/i18n/navigation";
import { cn } from "@/lib/format";

/** Icons available to portal navigation (names are passed from server components). */
export const portalIcons = {
  BarChart3, Bell, BookOpen, Building2, CalendarDays, CircleHelp, ClipboardList, Eye, FileText, Flag, Gavel, Globe2, Heart, ImageIcon, KeyRound,
  Languages, LayoutDashboard, ListChecks, Mail, Megaphone, MessageSquare, Phone, ScrollText, Settings, Shapes, ShieldCheck, Sparkles, Star, Store,
  Trophy, UserRound, Users, Wallet,
};
export type PortalIcon = keyof typeof portalIcons;
export type PortalNavItem = { href: string; label: string; icon: PortalIcon; exact?: boolean };
export type PortalNavGroup = { label?: string; items: PortalNavItem[] };

const active = (pathname: string, item: PortalNavItem) => (item.exact ? pathname === item.href : pathname === item.href || pathname.startsWith(item.href + "/"));

export function PortalNav({ groups, label }: { groups: PortalNavGroup[]; label: string }) {
  const pathname = usePathname();
  return (
    <>
      {/* Phones and tablets: one scrollable row. */}
      <nav aria-label={label} className="-mx-4 overflow-x-auto border-b border-line px-4 lg:hidden">
        <ul className="flex gap-1 py-2">
          {groups.flatMap((g) => g.items).map((item) => {
            const Icon = portalIcons[item.icon];
            const on = active(pathname, item);
            return (
              <li key={item.href} className="shrink-0">
                <Link href={item.href} aria-current={on ? "page" : undefined} className={cn("inline-flex h-9 items-center gap-1.5 rounded-md px-3 text-sm", on ? "bg-ink text-white" : "text-ink-2 hover:bg-sunken")}>
                  <Icon className="h-4 w-4" aria-hidden />
                  {item.label}
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>
      {/* Desktop: grouped sidebar. */}
      <nav aria-label={label} className="hidden lg:block">
        {groups.map((g, i) => (
          <div key={i} className="mb-5">
            {g.label && <p className="mb-1.5 px-3 text-xs font-semibold text-muted">{g.label}</p>}
            <ul className="space-y-0.5">
              {g.items.map((item) => {
                const Icon = portalIcons[item.icon];
                const on = active(pathname, item);
                return (
                  <li key={item.href}>
                    <Link
                      href={item.href}
                      aria-current={on ? "page" : undefined}
                      className={cn(
                        "flex h-9 items-center gap-2.5 rounded-md px-3 text-sm",
                        on ? "bg-patina-50 font-semibold text-patina-700 shadow-[inset_3px_0_0_var(--color-patina)] rtl:shadow-[inset_-3px_0_0_var(--color-patina)]" : "text-ink-2 hover:bg-sunken hover:text-ink",
                      )}
                    >
                      <Icon className="h-4 w-4 shrink-0" aria-hidden />
                      {item.label}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </nav>
    </>
  );
}
