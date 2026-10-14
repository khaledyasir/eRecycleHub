import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuthCard, ResetForm } from "@/components/public/auth-forms";
import { Alert } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("auth.reset");
  return { title: t("title"), robots: { index: false }, referrer: "no-referrer" };
}

export default async function ResetPasswordPage({ searchParams }: { searchParams: Promise<{ token?: string }> }) {
  const [{ token }, t, client] = await Promise.all([searchParams, getTranslations("auth.reset"), serverApi()]);
  const valid = token ? (await client.GET("/api/auth/reset-password/validate", { params: { query: { token } } })).data?.valid === true : false;
  if (!valid || !token)
    return (
      <AuthCard title={t("invalidTitle")}>
        <div className="space-y-4">
          <Alert tone="danger">{t("invalid")}</Alert>
          <Link href="/forgot-password" className="text-sm font-medium text-patina hover:underline">
            {t("requestNew")}
          </Link>
        </div>
      </AuthCard>
    );
  return (
    <AuthCard title={t("title")}>
      <ResetForm token={token} />
    </AuthCard>
  );
}
