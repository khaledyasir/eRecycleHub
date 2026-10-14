import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { AuthCard, RegisterForm } from "@/components/public/auth-forms";
import { Link, redirect } from "@/i18n/navigation";
import { getCountryId, getSession, load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("auth.register");
  return { title: t("title"), description: t("description"), robots: { index: false } };
}

export default async function RegisterPage() {
  const [user, locale, t, client, countryId] = await Promise.all([getSession(), getLocale(), getTranslations("auth.register"), serverApi(), getCountryId()]);
  if (user) return redirect({ href: "/account", locale });
  const countries = await load(client.GET("/api/countries"));
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
      <RegisterForm countries={countries} defaultCountry={countryId} />
    </AuthCard>
  );
}
