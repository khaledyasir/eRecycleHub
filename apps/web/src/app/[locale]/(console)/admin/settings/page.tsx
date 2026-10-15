import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.settings"))("title") };
}

export default async function Page() {
  const [t, f, client, labels] = await Promise.all([getTranslations("cms.settings"), getTranslations("cms.fields"), serverApi(), cmsLabels("settings")]);
  const rows = await load(client.GET("/api/admin/settings"));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <CmsManager
        path="/api/admin/settings"
        multipart={false}
        labels={labels}
        titleKeys={["titleEn", "key"]}
        defaults={{}}
        fields={[
          { name: "key", label: f("key"), kind: "text", required: true, hint: f("keyHint") },
          { name: "titleEn", label: f("titleEn"), kind: "text" },
          { name: "titleAr", label: f("titleAr"), kind: "text", rtl: true },
          { name: "valueEn", label: f("valueEn"), kind: "rich" },
          { name: "valueAr", label: f("valueAr"), kind: "rich", rtl: true },
        ]}
        columns={[
          { key: "key", label: f("key") },
          { key: "titleEn", label: f("titleEn") },
          { key: "titleAr", label: f("titleAr") },
        ]}
        rows={rows}
      />
    </>
  );
}
