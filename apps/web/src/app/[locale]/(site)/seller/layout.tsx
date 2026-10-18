import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import type { ReactNode } from "react";
import type { PortalNavGroup } from "@/components/portal/portal-nav";
import { PortalShell } from "@/components/portal/portal-shell";
import { load, serverApi } from "@/lib/api/server";
import { requireSeller } from "@/lib/guard";

export const metadata: Metadata = { robots: { index: false } };

export default async function SellerLayout({ children }: { children: ReactNode }) {
  const user = await requireSeller();
  const [t, client] = await Promise.all([getTranslations("seller.nav"), serverApi()]);
  const profile = await load(client.GET("/api/seller/profile"));
  const groups: PortalNavGroup[] = [
    {
      label: t("selling"),
      items: [
        { href: "/seller", label: t("dashboard"), icon: "LayoutDashboard", exact: true },
        { href: "/seller/auctions", label: t("auctions"), icon: "Gavel" },
        { href: "/seller/reviews", label: t("reviews"), icon: "Star" },
        { href: "/seller/bidders", label: t("bidders"), icon: "Users" },
      ],
    },
    {
      label: t("account"),
      items: [
        { href: "/seller/profile", label: t("profile"), icon: "Store" },
        { href: "/seller/password", label: t("password"), icon: "KeyRound" },
        { href: `/stores/${profile.publisherId}`, label: t("store"), icon: "Globe2" },
        { href: "/account", label: t("bidding"), icon: "UserRound" },
      ],
    },
  ];
  const name = [profile.firstName, profile.lastName].filter(Boolean).join(" ");
  return (
    <PortalShell title={t("title")} subtitle={name || user.fullName} groups={groups} navLabel={t("label")}>
      {children}
    </PortalShell>
  );
}
