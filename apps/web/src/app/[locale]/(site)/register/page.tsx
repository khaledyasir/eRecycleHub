import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { AuthCard, RegisterForm } from "@/components/public/auth-forms";
import { Link, redirect } from "@/i18n/navigation";
import { getSession } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("auth.register");
  return { title: t("title"), description: t("description"), robots: { index: false } };
}

export default async function RegisterPage() {
  const [user, locale, t] = await Promise.all([getSession(), getLocale(), getTranslations("auth.register")]);
  if (user) return redirect({ href: "/account", locale });
  return (
    <AuthCard
      title={t("title")}
      description={t("description")}
      footer={
        <p>
          {t("haveAccount")}{" "}
          <Link href="/login" className="font-medium text-patina hover:underline">
            {t("signIn")}
          </Link>
        </p>
      }
    >
      <RegisterForm />
    </AuthCard>
  );
}
