import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { alternates } from "@/lib/seo";
import { HowToPage } from "@/components/public/content-pages";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.howTo"), getLocale()]);
  return { title: t("sellTitle"), description: t("sellDescription"), alternates: alternates(locale, "/how-to-sell") };
}

export default function Page() {
  return <HowToPage kind="sell" />;
}
