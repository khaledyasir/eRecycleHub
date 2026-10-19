import { FileText, Gavel, Mail, MapPin, Phone, PlayCircle, Recycle, Search, Trophy, Truck, UserPlus, Wallet } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { RichHtml } from "@/components/rich-html";
import { EmptyState } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { getCountryId, load, loadOptional, serverApi } from "@/lib/api/server";
import type { Schema } from "@/lib/api/types";
import { pick } from "@/lib/format";
import { ContentPage } from "./content";

const STEP_ICONS = [UserPlus, Wallet, Search, Gavel, Trophy, Truck];
const STEP_TONES = ["bg-patina-50 text-patina", "bg-purple/10 text-purple", "bg-orange/10 text-orange", "bg-pink/10 text-pink"];

/** Steps from the CMS ("How to bid"). Legacy used the same steps for both the buying and the selling guide. */
export async function HowToPage({ kind }: { kind: "buy" | "sell" }) {
  const [t, locale, client] = await Promise.all([getTranslations("public.howTo"), getLocale(), serverApi()]);
  const data = await load(client.GET("/api/content/how-to"));
  const buy = kind === "buy";
  const cta = "inline-flex h-12 items-center justify-center gap-2 rounded-2xl px-7 font-extrabold transition hover:-translate-y-0.5 motion-reduce:transform-none";
  return (
    <>
      <section className="soft-hero relative overflow-hidden">
        <div className="container-page grid items-center gap-8 py-12 md:grid-cols-[1.2fr_1fr] lg:py-16">
          <div>
            <span className="inline-flex items-center gap-2 rounded-full border border-patina-100 bg-surface/90 px-4 py-2 text-xs font-extrabold text-patina">
              <Recycle className="h-4 w-4" aria-hidden /> {t("kicker", { count: data.steps.length })}
            </span>
            <h1 className="mt-5 text-3xl font-black sm:text-4xl">{buy ? t("buyTitle") : t("sellTitle")}</h1>
            <p className="mt-4 max-w-xl text-base leading-8 text-ink-2 sm:text-lg">{buy ? t("buyDescription") : t("sellDescription")}</p>
          </div>
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src="/images/recycling-hero.svg" alt="" className="mx-auto hidden max-h-64 w-full rounded-[1.5rem] object-cover md:block" />
        </div>
      </section>

      <div className="container-page py-12">
        {data.steps.length === 0 ? (
          <EmptyState title={t("empty")} />
        ) : (
          <ol className="relative mx-auto max-w-3xl space-y-6 before:absolute before:inset-y-6 before:start-7 before:w-0.5 before:bg-line sm:before:start-8">
            {data.steps.map((s, i) => {
              const Icon = STEP_ICONS[i % STEP_ICONS.length];
              return (
                <li key={s.id} className="relative flex gap-4 sm:gap-6">
                  <span className={`relative z-10 grid h-14 w-14 shrink-0 place-items-center rounded-2xl border-4 border-ground sm:h-16 sm:w-16 ${STEP_TONES[i % STEP_TONES.length]}`} aria-hidden>
                    <Icon className="h-6 w-6 sm:h-7 sm:w-7" />
                  </span>
                  <div className="min-w-0 flex-1 rounded-[1.5rem] border border-line bg-surface p-5 shadow-sm sm:p-6">
                    <p className="tabular text-xs font-extrabold text-patina">{t("step", { n: i + 1 })}</p>
                    <h2 className="mt-1 text-lg font-black sm:text-xl">{pick(locale, s.titleEn, s.titleAr)}</h2>
                    <RichHtml html={pick(locale, s.descEn, s.descAr)} className="mt-2 text-sm leading-7 text-ink-2" />
                  </div>
                </li>
              );
            })}
          </ol>
        )}

        <div className="mx-auto mt-12 flex max-w-3xl flex-col items-start justify-between gap-5 rounded-[2rem] bg-patina p-7 text-white sm:flex-row sm:items-center sm:p-10">
          <p className="text-xl font-black">{buy ? t("buyCtaText") : t("sellCtaText")}</p>
          <div className="flex flex-wrap gap-3">
            {data.guidelinesVideoUrl && (
              <a href={data.guidelinesVideoUrl} target="_blank" rel="noopener noreferrer" className={`${cta} border border-white/40 hover:bg-white/10`}>
                <PlayCircle className="h-5 w-5" aria-hidden /> {t("video")}
              </a>
            )}
            <Link href={buy ? "/auctions" : "/apply"} className={`${cta} bg-surface text-patina hover:bg-patina-50`}>
              {buy ? t("browseCta") : t("applyCta")}
            </Link>
          </div>
        </div>
      </div>
    </>
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
