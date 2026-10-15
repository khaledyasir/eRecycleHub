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

export const isActive = (pathname: string, item: PortalNavItem) => (item.exact ? pathname === item.href : pathname === item.href || pathname.startsWith(item.href + "/"));

/** `tabs`: one wrapping row of pills (account, seller). `sidebar`: grouped vertical list (admin console). */
export function PortalNav({ groups, label, variant = "tabs", onNavigate }: { groups: PortalNavGroup[]; label: string; variant?: "tabs" | "sidebar"; onNavigate?: () => void }) {
  const pathname = usePathname();
  if (variant === "tabs") {
    return (
      <nav aria-label={label} className="mb-7 flex flex-wrap gap-2 rounded-xl border border-line bg-surface p-3 shadow-sm">
        {groups.flatMap((g) => g.items).map((item) => {
          const Icon = portalIcons[item.icon];
          const on = isActive(pathname, item);
          return (
            <Link
              key={item.href}
              href={item.href}
              aria-current={on ? "page" : undefined}
              className={cn(
                "inline-flex items-center gap-2 rounded-md px-4 py-3 text-sm font-bold transition",
                on ? "bg-patina text-white shadow-sm" : "text-ink-2 hover:bg-patina-50 hover:text-patina",
              )}
            >
              <Icon className="h-4 w-4" aria-hidden />
              {item.label}
            </Link>
          );
        })}
      </nav>
    );
  }
  return (
    <nav aria-label={label}>
      {groups.map((g, i) => (
        <div key={i} className="mb-5">
          {g.label && <p className="mb-2 px-4 text-xs font-bold text-muted">{g.label}</p>}
          <ul className="space-y-1">
            {g.items.map((item) => {
              const Icon = portalIcons[item.icon];
              const on = isActive(pathname, item);
              return (
                <li key={item.href}>
                  <Link
                    href={item.href}
                    onClick={onNavigate}
                    aria-current={on ? "page" : undefined}
                    className={cn("flex items-center gap-3 rounded-md px-4 py-3 text-sm font-bold transition", on ? "bg-patina-50 text-patina" : "text-ink-2 hover:bg-sunken")}
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
  );
}
