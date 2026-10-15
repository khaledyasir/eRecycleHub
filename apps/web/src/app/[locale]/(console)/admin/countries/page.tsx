import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.countries"))("title") };
}

export default async function Page() {
  const [t, f, client, labels] = await Promise.all([getTranslations("cms.countries"), getTranslations("cms.fields"), serverApi(), cmsLabels("countries")]);
  const rows = await load(client.GET("/api/admin/countries"));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <CmsManager
        path="/api/admin/countries"
        multipart={true}
        labels={labels}
        titleKeys={["nameEn", "nameAr"]}
        defaults={{ isActive: true }}
        fields={[
          { name: "nameEn", label: f("nameEn"), kind: "text", required: true },
          { name: "nameAr", label: f("nameAr"), kind: "text", required: true, rtl: true },
          { name: "code", label: f("code"), kind: "text", hint: "JO, SA, AE…" },
          { name: "isActive", label: f("isActive"), kind: "bool" },
          { name: "flag", label: f("flag"), kind: "file", current: "flagFileId" },
        ]}
        columns={[
          { key: "flagFileId", label: f("flag"), kind: "file" },
          { key: "nameEn", label: f("nameEn") },
          { key: "nameAr", label: f("nameAr") },
          { key: "code", label: f("code") },
          { key: "isActive", label: f("isActive"), kind: "bool" },
        ]}
        rows={rows}
      />
    </>
  );
}
