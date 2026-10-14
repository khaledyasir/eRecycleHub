import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { alternates } from "@/lib/seo";
import { ContentPage } from "@/components/public/content";
import { RichHtml } from "@/components/rich-html";
import { EmptyState } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { fileUrl, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.benefits"), getLocale()]);
  return { title: t("title"), description: t("description"), alternates: alternates(locale, "/benefits") };
}

export default async function BenefitsPage() {
  const [t, tc, locale, client] = await Promise.all([getTranslations("public.benefits"), getTranslations("common"), getLocale(), serverApi()]);
  const items = await load(client.GET("/api/content/benefits"));
  return (
    <ContentPage title={t("title")} description={t("description")} wide>
      {items.length === 0 ? (
        <EmptyState title={t("empty")} />
      ) : (
        <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {items.map((b) => (
            <li key={b.id} className="rounded-lg border border-line bg-surface p-5">
              {b.iconFileId && (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={fileUrl(b.iconFileId)} alt="" className="mb-3 h-12 w-12 rounded-md" />
              )}
              <h2 className="text-lg font-semibold">{pick(locale, b.titleEn, b.titleAr)}</h2>
              <RichHtml html={pick(locale, b.descEn, b.descAr)} className="mt-1 text-sm" />
            </li>
          ))}
        </ul>
      )}
      <div className="mt-8 flex flex-wrap gap-3">
        <Link href="/register" className="inline-flex h-11 items-center rounded-md bg-patina px-4 font-medium text-white hover:bg-patina-700">{tc("user.register")}</Link>
        <Link href="/apply" className="inline-flex h-11 items-center rounded-md border border-line-strong bg-surface px-4 font-medium hover:bg-sunken">{tc("nav.apply")}</Link>
      </div>
    </ContentPage>
  );
}
