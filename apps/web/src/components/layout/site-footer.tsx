import { Mail, MapPin, Phone } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Link } from "@/i18n/navigation";
import { getCountryId, loadOptional, serverApi } from "@/lib/api/server";
import type { Schema } from "@/lib/api/types";
import { DISPLAY_TZ } from "@/lib/format";
import { Logo } from "./logo";

export async function SiteFooter() {
  const t = await getTranslations("common");
  const client = await serverApi();
  const countryId = await getCountryId();
  const info = await loadOptional<Schema<"ContactInfoDto">>(client.GET("/api/content/contact-info", { params: { query: { countryId } } }));
  const columns = [
    { title: t("footer.marketplace"), links: [["/auctions", t("nav.auctions")], ["/apply", t("nav.apply")], ["/benefits", t("nav.benefits")], ["/clients", t("nav.clients")]] },
    { title: t("footer.company"), links: [["/about", t("nav.about")], ["/advertise", t("nav.advertise")], ["/terms", t("nav.terms")], ["/privacy", t("nav.privacy")]] },
    { title: t("footer.help"), links: [["/how-to-buy", t("nav.howToBuy")], ["/how-to-sell", t("nav.howToSell")], ["/faq", t("nav.faq")], ["/support", t("nav.support")], ["/contact", t("nav.contact")]] },
  ];
  const social = (
    [
      ["Facebook", info?.facebook],
      ["Instagram", info?.instagram],
      ["X / Twitter", info?.twitter],
      ["LinkedIn", info?.linkedin],
    ] as const
  ).filter(([, url]) => !!url);
  return (
    <footer className="mt-20 bg-footer text-white/80">
      <div className="container-page grid gap-10 py-12 md:grid-cols-[1.4fr_repeat(3,1fr)]">
        <div className="space-y-4">
          <Logo inverted />
          <p className="max-w-sm text-sm text-white/70">{t("footer.about")}</p>
          {info && (
            <ul className="space-y-2 text-sm">
              {info.mobile && (
                <li className="flex items-center gap-2">
                  <Phone className="h-4 w-4 text-cyan" aria-hidden />
                  <a href={`tel:${info.mobile.replace(/\s/g, "")}`} className="hover:text-white" dir="ltr">
                    {info.mobile}
                  </a>
                </li>
              )}
              {info.email && (
                <li className="flex items-center gap-2">
                  <Mail className="h-4 w-4 text-cyan" aria-hidden />
                  <a href={`mailto:${info.email}`} className="hover:text-white">
                    {info.email}
                  </a>
                </li>
              )}
              {info.address && (
                <li className="flex items-center gap-2">
                  <MapPin className="h-4 w-4 text-cyan" aria-hidden />
                  {info.address}
                </li>
              )}
            </ul>
          )}
        </div>
        {columns.map((c) => (
          <nav key={c.title} aria-label={c.title}>
            <h2 className="mb-5 font-extrabold text-white">{c.title}</h2>
            <ul className="space-y-2 text-sm">
              {c.links.map(([href, label]) => (
                <li key={href}>
                  <Link href={href} className="hover:text-white hover:underline">
                    {label}
                  </Link>
                </li>
              ))}
            </ul>
          </nav>
        ))}
      </div>
      <div className="brand-gradient h-1" aria-hidden />
      <div className="border-t border-white/10">
        <div className="container-page flex flex-col gap-3 py-5 text-xs text-white/60 sm:flex-row sm:items-center sm:justify-between">
          <p>
            {t("footer.rights", { year: new Date().getFullYear() })} {t("timeZoneNote", { zone: DISPLAY_TZ })}
          </p>
          <div className="flex flex-wrap items-center gap-4">
            {social.map(([label, url]) => (
              <a key={label} href={url!} rel="noopener noreferrer" target="_blank" className="hover:text-white">
                {label}
              </a>
            ))}
            <a href="https://www.tenderjo.com/" rel="noopener" target="_blank" className="hover:text-white">
              {t("footer.credit")}
            </a>
          </div>
        </div>
      </div>
    </footer>
  );
}
