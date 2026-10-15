import { ExternalLink } from "lucide-react";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.hero"))("title") };
}

export default async function Page() {
  const [t, f, fc, client, labels] = await Promise.all([
    getTranslations("cms.hero"), getTranslations("cms.hero.fields"), getTranslations("cms.fields"), serverApi(), cmsLabels("hero"),
  ]);
  const rows = await load(client.GET("/api/admin/hero"));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <Link href="/" target="_blank" className="mb-4 inline-flex items-center gap-2 text-sm font-bold text-patina hover:underline">
        <ExternalLink className="h-4 w-4" aria-hidden />
        {t("preview")}
      </Link>
      <CmsManager
        path="/api/admin/hero"
        multipart={true}
        single
        labels={labels}
        titleKeys={["titleEn", "titleAr"]}
        defaults={{}}
        fields={[
          { name: "badgeEn", label: f("badgeEn"), kind: "text" },
          { name: "badgeAr", label: f("badgeAr"), kind: "text", rtl: true },
          { name: "titleEn", label: f("titleEn"), kind: "area", hint: f("titleHint") },
          { name: "titleAr", label: f("titleAr"), kind: "area", rtl: true, hint: f("titleHint") },
          { name: "leadEn", label: f("leadEn"), kind: "area" },
          { name: "leadAr", label: f("leadAr"), kind: "area", rtl: true },
          { name: "primaryCtaEn", label: f("primaryCtaEn"), kind: "text" },
          { name: "primaryCtaAr", label: f("primaryCtaAr"), kind: "text", rtl: true },
          { name: "primaryCtaUrl", label: f("primaryCtaUrl"), kind: "url", hint: f("ctaUrlHint") },
          { name: "secondaryCtaUrl", label: f("secondaryCtaUrl"), kind: "url", hint: f("ctaUrlHint") },
          { name: "secondaryCtaEn", label: f("secondaryCtaEn"), kind: "text" },
          { name: "secondaryCtaAr", label: f("secondaryCtaAr"), kind: "text", rtl: true },
          { name: "cardTitleEn", label: f("cardTitleEn"), kind: "text" },
          { name: "cardTitleAr", label: f("cardTitleAr"), kind: "text", rtl: true },
          { name: "image", label: f("image"), kind: "file", current: "imageFileId", hint: f("imageHint") },
          { name: "imageAltEn", label: f("imageAltEn"), kind: "text", hint: f("altHint") },
          { name: "imageAltAr", label: f("imageAltAr"), kind: "text", rtl: true, hint: f("altHint") },
          { name: "showSearch", label: f("showSearch"), kind: "bool" },
          { name: "isActive", label: f("isActive"), kind: "bool" },
        ]}
        columns={[
          { key: "imageFileId", label: f("image"), kind: "file" },
          { key: "titleEn", label: f("titleEn") },
          { key: "titleAr", label: f("titleAr") },
          { key: "isActive", label: fc("isActive"), kind: "bool" },
        ]}
        rows={rows}
      />
    </>
  );
}
