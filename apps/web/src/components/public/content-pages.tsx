import { FileText, Mail, MapPin, Phone, PlayCircle } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { RichHtml } from "@/components/rich-html";
import { EmptyState } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { getCountryId, load, loadOptional, serverApi } from "@/lib/api/server";
import type { Schema } from "@/lib/api/types";
import { pick } from "@/lib/format";
import { ContentPage } from "./content";

/** Steps from the CMS ("How to bid"). Legacy used the same steps for both the buying and the selling guide. */
export async function HowToPage({ kind }: { kind: "buy" | "sell" }) {
  const [t, locale, client] = await Promise.all([getTranslations("public.howTo"), getLocale(), serverApi()]);
  const data = await load(client.GET("/api/content/how-to"));
  return (
    <ContentPage title={kind === "buy" ? t("buyTitle") : t("sellTitle")} description={kind === "buy" ? t("buyDescription") : t("sellDescription")}>
      {data.steps.length === 0 ? (
        <EmptyState title={t("empty")} />
      ) : (
        <ol className="space-y-4">
          {data.steps.map((s, i) => (
            <li key={s.id} className="flex gap-4 rounded-lg border border-line bg-surface p-5">
              <span className="tabular inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-patina font-semibold text-white" aria-hidden>
                {i + 1}
              </span>
              <div className="min-w-0">
                <p className="text-xs text-muted">{t("step", { n: i + 1 })}</p>
                <h2 className="text-lg font-semibold">{pick(locale, s.titleEn, s.titleAr)}</h2>
                <RichHtml html={pick(locale, s.descEn, s.descAr)} className="mt-1" />
              </div>
            </li>
          ))}
        </ol>
      )}
      <div className="mt-8 flex flex-wrap gap-3">
        {data.guidelinesVideoUrl && (
          <a href={data.guidelinesVideoUrl} target="_blank" rel="noopener noreferrer" className="inline-flex h-11 items-center gap-2 rounded-md border border-line-strong bg-surface px-4 font-medium hover:bg-sunken">
            <PlayCircle className="h-5 w-5 text-patina" aria-hidden /> {t("video")}
          </a>
        )}
        <Link href={kind === "buy" ? "/auctions" : "/apply"} className="inline-flex h-11 items-center rounded-md bg-patina px-4 font-medium text-white hover:bg-patina-700">
          {kind === "buy" ? t("browseCta") : t("applyCta")}
        </Link>
      </div>
    </ContentPage>
  );
}

/** Terms / privacy: bilingual pages stored as settings. */
export async function LegalPage({ settingKey, title }: { settingKey: string; title: string }) {
  const [t, locale, client] = await Promise.all([getTranslations("public.legal"), getLocale(), serverApi()]);
  const s = await loadOptional<Schema<"SettingDto">>(client.GET("/api/content/settings/{key}", { params: { path: { key: settingKey } } }));
  return (
    <ContentPage title={pick(locale, s?.titleEn, s?.titleAr) || title}>
      {s && pick(locale, s.valueEn, s.valueAr) ? <RichHtml html={pick(locale, s.valueEn, s.valueAr)} /> : <EmptyState icon={<FileText className="h-8 w-8" />} title={t("empty")} />}
    </ContentPage>
  );
}

/** Contact details for the visitor's country (falls back to the first configured row, like the legacy site). */
export async function ContactDetails() {
  const [t, client, countryId] = await Promise.all([getTranslations("public.contact"), serverApi(), getCountryId()]);
  const info = await loadOptional<Schema<"ContactInfoDto">>(client.GET("/api/content/contact-info", { params: { query: { countryId } } }));
  if (!info) return null;
  return (
    <section className="rounded-lg border border-line bg-surface p-5" aria-labelledby="contact-details">
      <h2 id="contact-details" className="mb-3 font-semibold">
        {t("details")}
      </h2>
      <ul className="space-y-2 text-sm">
        {info.mobile && (
          <li className="flex items-center gap-2">
            <Phone className="h-4 w-4 text-patina" aria-hidden />
            <a href={`tel:${info.mobile.replace(/\s/g, "")}`} className="hover:underline" dir="ltr">
              {info.mobile}
            </a>
          </li>
        )}
        {info.email && (
          <li className="flex items-center gap-2">
            <Mail className="h-4 w-4 text-patina" aria-hidden />
            <a href={`mailto:${info.email}`} className="hover:underline">
              {info.email}
            </a>
          </li>
        )}
        {info.address && (
          <li className="flex items-center gap-2">
            <MapPin className="h-4 w-4 text-patina" aria-hidden />
            {info.address}
          </li>
        )}
      </ul>
      {info.mapUrl?.startsWith("https://www.google.com/maps") && (
        <iframe src={info.mapUrl} title={t("map")} className="mt-4 h-64 w-full rounded-md border-0" loading="lazy" referrerPolicy="no-referrer-when-downgrade" />
      )}
    </section>
  );
}
