import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";
import { pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.contactInfo"))("title") };
}

export default async function Page() {
  const [t, f, locale, client, labels] = await Promise.all([getTranslations("cms.contactInfo"), getTranslations("cms.fields"), getLocale(), serverApi(), cmsLabels("contactInfo")]);
  const rows = await load(client.GET("/api/admin/contact-info"));
  const countries = await load(client.GET("/api/admin/countries"));
  const countryNames = Object.fromEntries(countries.map((c) => [String(c.id), pick(locale, c.nameEn, c.nameAr)]));
  const countryOptions = countries.map((c) => ({ value: c.id, label: pick(locale, c.nameEn, c.nameAr) }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <CmsManager
        path="/api/admin/contact-info"
        multipart={false}
        labels={labels}
        titleKeys={["address", "email"]}
        defaults={{}}
        fields={[
          { name: "countryId", label: f("countryId"), kind: "select", required: true, options: countryOptions },
          { name: "mobile", label: f("mobile"), kind: "text" },
          { name: "email", label: f("email"), kind: "text" },
          { name: "address", label: f("address"), kind: "area" },
          { name: "mapUrl", label: f("mapUrl"), kind: "url" },
          { name: "facebook", label: f("facebook"), kind: "url" },
          { name: "instagram", label: f("instagram"), kind: "url" },
          { name: "twitter", label: f("twitter"), kind: "url" },
          { name: "linkedin", label: f("linkedin"), kind: "url" },
        ]}
        columns={[
          { key: "mobile", label: f("mobile") },
          { key: "email", label: f("email") },
          { key: "address", label: f("address") },
          { key: "countryId", label: f("countryName"), kind: "option", options: countryNames },
        ]}
        rows={rows}
      />
    </>
  );
}
