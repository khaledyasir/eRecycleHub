import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import type { ReactNode } from "react";
import { PortalShell } from "@/components/portal/portal-shell";
import type { PortalNavGroup } from "@/components/portal/portal-nav";
import { requireRole } from "@/lib/guard";

export const metadata: Metadata = { robots: { index: false } };

export default async function AccountLayout({ children }: { children: ReactNode }) {
  // Administrators have no customer account; publishers do (they may bid on other stores' lots).
  const user = await requireRole(["Customer"]);
  const t = await getTranslations("account.nav");
  const groups: PortalNavGroup[] = [
    {
      label: t("bidding"),
      items: [
        { href: "/account", label: t("dashboard"), icon: "LayoutDashboard", exact: true },
        { href: "/account/auctions", label: t("auctions"), icon: "Gavel" },
        { href: "/account/calendar", label: t("calendar"), icon: "CalendarDays" },
        { href: "/account/favorites", label: t("favorites"), icon: "Heart" },
        { href: "/account/watching", label: t("watching"), icon: "Eye" },
      ],
    },
    {
      label: t("account"),
      items: [
        { href: "/account/wallet", label: t("wallet"), icon: "Wallet" },
        { href: "/account/settings", label: t("settings"), icon: "UserRound" },
        ...(user.publisherId != null ? [{ href: "/seller", label: t("seller"), icon: "Store" as const }] : []),
      ],
    },
  ];
  return (
    <PortalShell title={t("title")} subtitle={user.fullName} groups={groups} navLabel={t("label")} cta={user.publisherId == null ? { href: "/account/become-seller", label: t("becomeSeller"), hint: t("becomeSellerHint") } : undefined}>
      {children}
    </PortalShell>
  );
}
