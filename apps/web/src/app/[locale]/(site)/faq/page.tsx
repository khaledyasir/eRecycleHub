import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { alternates } from "@/lib/seo";
import { ContentPage } from "@/components/public/content";
import { FaqList } from "@/components/public/public-forms";
import { EmptyState } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.faq"), getLocale()]);
  return { title: t("title"), description: t("description"), alternates: alternates(locale, "/faq") };
}

export default async function FaqPage() {
  const [t, locale, client] = await Promise.all([getTranslations("public.faq"), getLocale(), serverApi()]);
  const groups = await load(client.GET("/api/content/faq"));
  return (
    <ContentPage title={t("title")} description={t("description")}>
      {groups.length === 0 ? (
        <EmptyState title={t("empty")} />
      ) : (
        <FaqList groups={groups.map((g) => ({ id: g.id, title: pick(locale, g.titleEn, g.titleAr), faqs: g.faqs.map((f) => ({ id: f.id, question: pick(locale, f.questionEn, f.questionAr), answer: pick(locale, f.answerEn, f.answerAr) })) }))} />
      )}
      <p className="mt-10 text-ink-2">
        {t("stillNeed")}{" "}
        <Link href="/contact" className="font-medium text-patina hover:underline">{t("contact")}</Link>
      </p>
    </ContentPage>
  );
}
