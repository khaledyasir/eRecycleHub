import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuthCard, ForgotForm } from "@/components/public/auth-forms";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("auth.forgot");
  return { title: t("title"), robots: { index: false } };
}

export default async function ForgotPasswordPage() {
  const t = await getTranslations("auth.forgot");
  return (
    <AuthCard title={t("title")} description={t("description")}>
      <ForgotForm />
    </AuthCard>
  );
}
