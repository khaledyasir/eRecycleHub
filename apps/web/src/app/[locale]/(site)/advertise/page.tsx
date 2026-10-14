import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { alternates } from "@/lib/seo";
import { ContentPage } from "@/components/public/content";
import { ContactDetails } from "@/components/public/content-pages";
import { Link } from "@/i18n/navigation";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.advertise"), getLocale()]);
  return { title: t("title"), description: t("body"), alternates: alternates(locale, "/advertise") };
}

export default async function AdvertisePage() {
  const [t, tc] = await Promise.all([getTranslations("public.advertise"), getTranslations("common")]);
  return (
    <ContentPage title={t("title")} description={t("body")}>
      <div className="space-y-6">
        <ContactDetails />
        <Link href="/contact" className="inline-flex h-11 items-center rounded-md bg-patina px-4 font-medium text-white hover:bg-patina-700">{tc("nav.contact")}</Link>
      </div>
    </ContentPage>
  );
}
