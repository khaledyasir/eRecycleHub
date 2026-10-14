import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { alternates } from "@/lib/seo";
import { ContentPage } from "@/components/public/content";
import { ContactDetails } from "@/components/public/content-pages";
import { ContactForm } from "@/components/public/public-forms";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.contact"), getLocale()]);
  return { title: t("title"), description: t("description"), alternates: alternates(locale, "/contact") };
}

export default async function ContactPage() {
  const t = await getTranslations("public.contact");
  return (
    <ContentPage title={t("title")} description={t("description")} wide>
      <div className="grid gap-8 lg:grid-cols-[1fr_24rem]">
        <div className="rounded-xl border border-line bg-surface p-5 sm:p-7">
          <ContactForm />
        </div>
        <ContactDetails />
      </div>
    </ContentPage>
  );
}
