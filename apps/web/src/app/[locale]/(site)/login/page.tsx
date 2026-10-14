import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { AuthCard, homeFor, LoginForm, safeNext } from "@/components/public/auth-forms";
import { Link, redirect } from "@/i18n/navigation";
import { getSession } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("auth.login");
  return { title: t("title"), robots: { index: false } };
}

export default async function LoginPage({ searchParams }: { searchParams: Promise<{ next?: string }> }) {
  const [{ next }, user, locale, t] = await Promise.all([searchParams, getSession(), getLocale(), getTranslations("auth.login")]);
  if (user) return redirect({ href: safeNext(next) ?? homeFor(user.role), locale });
  return (
    <AuthCard
      title={t("title")}
      description={t("description")}
      footer={
        <>
          <p>
            {t("noAccount")}{" "}
            <Link href="/register" className="font-medium text-patina hover:underline">
              {t("register")}
            </Link>
          </p>
          <p>
            {t("seller")}{" "}
            <Link href="/apply" className="font-medium text-patina hover:underline">
              {t("apply")}
            </Link>
          </p>
        </>
      }
    >
      <LoginForm next={next} />
    </AuthCard>
  );
}
