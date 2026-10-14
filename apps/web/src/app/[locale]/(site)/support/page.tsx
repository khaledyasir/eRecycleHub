import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { alternates } from "@/lib/seo";
import { ContactDetails } from "@/components/public/content-pages";
import { ContentPage } from "@/components/public/content";
import { FaqList } from "@/components/public/public-forms";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.support"), getLocale()]);
  return { title: t("title"), description: t("description"), alternates: alternates(locale, "/support") };
}

export default async function SupportPage() {
  const [t, tf, tc, locale, client] = await Promise.all([getTranslations("public.support"), getTranslations("public.faq"), getTranslations("common"), getLocale(), serverApi()]);
  const groups = await load(client.GET("/api/content/faq"));
  const top = groups.map((g) => ({ id: g.id, title: pick(locale, g.titleEn, g.titleAr), faqs: g.faqs.slice(0, 4).map((f) => ({ id: f.id, question: pick(locale, f.questionEn, f.questionAr), answer: pick(locale, f.answerEn, f.answerAr) })) }));
  return (
    <ContentPage title={t("title")} description={t("description")} wide>
      <div className="grid gap-10 lg:grid-cols-[1fr_22rem]">
        <section aria-labelledby="topics">
          <h2 id="topics" className="mb-4 text-xl font-semibold">{t("topics")}</h2>
          {top.length === 0 ? <p className="text-muted">{tf("empty")}</p> : <FaqList groups={top} />}
          <Link href="/faq" className="mt-6 inline-block font-medium text-patina hover:underline">{tc("viewAll")}</Link>
        </section>
        <aside className="space-y-4">
          <section className="rounded-lg border border-line bg-surface p-5">
            <h2 className="font-semibold">{t("reach")}</h2>
            <p className="mt-1 text-sm text-muted">{t("reachBody")}</p>
            <Link href="/contact" className="mt-4 inline-flex h-10 items-center rounded-md bg-patina px-4 text-sm font-medium text-white hover:bg-patina-700">{tc("nav.contact")}</Link>
          </section>
          <ContactDetails />
        </aside>
      </div>
    </ContentPage>
  );
}
