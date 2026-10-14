import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { ApplyForm, AuthCard } from "@/components/public/auth-forms";
import { getCountryId, load, serverApi } from "@/lib/api/server";
import { alternates } from "@/lib/seo";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("auth.apply"), getLocale()]);
  return { title: t("title"), description: t("description"), alternates: alternates(locale, "/apply") };
}

export default async function ApplyPage() {
  const [t, client, countryId] = await Promise.all([getTranslations("auth.apply"), serverApi(), getCountryId()]);
  const countries = await load(client.GET("/api/countries"));
  return (
    <AuthCard title={t("title")} description={t("description")}>
      <ApplyForm countries={countries} defaultCountry={countryId} />
    </AuthCard>
  );
}
