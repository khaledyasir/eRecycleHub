import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { alternates } from "@/lib/seo";
import { ContentPage } from "@/components/public/content";
import { RichHtml } from "@/components/rich-html";
import { EmptyState } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";
import { fileUrl, number, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.about"), getLocale()]);
  return { title: t("title"), alternates: alternates(locale, "/about") };
}

export default async function AboutPage() {
  const [t, locale, client] = await Promise.all([getTranslations("public.about"), getLocale(), serverApi()]);
  const sections = await load(client.GET("/api/content/about"));
  return (
    <ContentPage title={t("title")} wide>
      {sections.length === 0 && <EmptyState title={t("empty")} />}
      <div className="space-y-12">
        {sections.map((s, i) => (
          <section key={s.id} className="grid items-start gap-8 lg:grid-cols-2">
            <div className={i % 2 ? "lg:order-2" : undefined}>
              {pick(locale, s.titleEn, s.titleAr) && <h2 className="text-2xl font-semibold">{pick(locale, s.titleEn, s.titleAr)}</h2>}
              <RichHtml html={pick(locale, s.descEn, s.descAr)} className="mt-3" />
              {s.totalBidders ? <p className="tabular mt-5 inline-block rounded-md bg-patina-50 px-3 py-2 font-semibold text-patina-700">{t("bidders", { count: number(s.totalBidders, locale) })}</p> : null}
            </div>
            <div className="grid gap-4">
              {[s.firstImageFileId, s.secondImageFileId].filter(Boolean).map((id) => (
                // eslint-disable-next-line @next/next/no-img-element
                <img key={id} src={fileUrl(id)} alt="" loading="lazy" className="w-full rounded-lg border border-line object-cover" />
              ))}
            </div>
          </section>
        ))}
      </div>
    </ContentPage>
  );
}
