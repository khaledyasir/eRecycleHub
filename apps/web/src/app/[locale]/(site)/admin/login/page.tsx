import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { AuthCard, LoginForm } from "@/components/public/auth-forms";
import { Link, redirect } from "@/i18n/navigation";
import { getSession } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.login"))("title"), robots: { index: false } };
}

export default async function AdminLogin() {
  const [user, locale, t] = await Promise.all([getSession(), getLocale(), getTranslations("admin.login")]);
  if (user?.role === "Admin") return redirect({ href: "/admin", locale });
  return (
    <AuthCard title={t("title")} description={t("description")} footer={<Link href="/" className="text-patina hover:underline">{t("site")}</Link>}>
      <LoginForm adminOnly next="/admin" />
    </AuthCard>
  );
}
