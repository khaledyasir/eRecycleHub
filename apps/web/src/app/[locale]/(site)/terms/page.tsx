import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { alternates } from "@/lib/seo";
import { LegalPage } from "@/components/public/content-pages";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.legal"), getLocale()]);
  return { title: t("termsTitle"), alternates: alternates(locale, "/terms") };
}

export default async function Page() {
  const t = await getTranslations("public.legal");
  return <LegalPage settingKey="terms" title={t("termsTitle")} />;
}
