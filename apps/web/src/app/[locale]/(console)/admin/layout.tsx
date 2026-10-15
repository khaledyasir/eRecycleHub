import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import type { ReactNode } from "react";
import type { PortalNavGroup } from "@/components/portal/portal-nav";
import { AdminShell } from "@/components/portal/admin-shell";
import { requireRole } from "@/lib/guard";

export const metadata: Metadata = { robots: { index: false } };

export default async function AdminLayout({ children }: { children: ReactNode }) {
  const user = await requireRole(["Admin"], "/admin/login");
  const t = await getTranslations("admin.nav");
  const groups: PortalNavGroup[] = [
    { label: t("overview"), items: [
      { href: "/admin", label: t("dashboard"), icon: "LayoutDashboard", exact: true },
      { href: "/admin/reports", label: t("reports"), icon: "BarChart3" },
      { href: "/admin/winners", label: t("winners"), icon: "Trophy" },
    ] },
    { label: t("marketplace"), items: [
      { href: "/admin/auctions", label: t("auctions"), icon: "Gavel" },
      { href: "/admin/applications", label: t("applications"), icon: "ClipboardList" },
      { href: "/admin/transactions", label: t("transactions"), icon: "Wallet" },
    ] },
    { label: t("people"), items: [
      { href: "/admin/users", label: t("users"), icon: "Users" },
      { href: "/admin/publishers", label: t("publishers"), icon: "Store" },
    ] },
    { label: t("content"), items: [
      { href: "/admin/hero", label: t("hero"), icon: "PanelTop" },
      { href: "/admin/categories", label: t("categories"), icon: "Shapes" },
      { href: "/admin/countries", label: t("countries"), icon: "Flag" },
      { href: "/admin/sliders", label: t("sliders"), icon: "ImageIcon" },
      { href: "/admin/announcements", label: t("announcements"), icon: "Megaphone" },
      { href: "/admin/about", label: t("about"), icon: "BookOpen" },
      { href: "/admin/benefits", label: t("benefits"), icon: "Sparkles" },
      { href: "/admin/how-to", label: t("howTo"), icon: "ListChecks" },
      { href: "/admin/faq", label: t("faq"), icon: "CircleHelp" },
      { href: "/admin/contact-info", label: t("contactInfo"), icon: "Phone" },
      { href: "/admin/messages", label: t("messages"), icon: "MessageSquare" },
      { href: "/admin/settings", label: t("settings"), icon: "FileText" },
      { href: "/admin/translations", label: t("translations"), icon: "Languages" },
    ] },
    { label: t("system"), items: [{ href: "/admin/audit", label: t("audit"), icon: "ScrollText" }] },
  ];
  return (
    <AdminShell title={t("title")} subtitle={user.email} groups={groups} navLabel={t("label")}>
      {children}
    </AdminShell>
  );
}
