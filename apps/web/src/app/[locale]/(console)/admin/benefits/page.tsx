import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.benefits"))("title") };
}

export default async function Page() {
  const [t, f, client, labels] = await Promise.all([getTranslations("cms.benefits"), getTranslations("cms.fields"), serverApi(), cmsLabels("benefits")]);
  const rows = await load(client.GET("/api/admin/benefits"));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <CmsManager
        path="/api/admin/benefits"
        multipart={true}
        labels={labels}
        titleKeys={["titleEn", "titleAr"]}
        defaults={{ sortOrder: "0", isActive: true }}
        fields={[
          { name: "titleEn", label: f("titleEn"), kind: "text", required: true },
          { name: "titleAr", label: f("titleAr"), kind: "text", required: true, rtl: true },
          { name: "descEn", label: f("descEn"), kind: "rich" },
          { name: "descAr", label: f("descAr"), kind: "rich", rtl: true },
          { name: "sortOrder", label: f("sortOrder"), kind: "number", hint: f("sortOrderHint") },
          { name: "isActive", label: f("isActive"), kind: "bool" },
          { name: "icon", label: f("icon"), kind: "file", current: "iconFileId" },
        ]}
        columns={[
          { key: "iconFileId", label: f("icon"), kind: "file" },
          { key: "titleEn", label: f("titleEn") },
          { key: "titleAr", label: f("titleAr") },
          { key: "sortOrder", label: f("sortOrder") },
          { key: "isActive", label: f("isActive"), kind: "bool" },
        ]}
        rows={rows}
      />
    </>
  );
}
