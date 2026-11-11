import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.announcements"))("title") };
}

export default async function Page() {
  const [t, f, client, labels] = await Promise.all([getTranslations("cms.announcements"), getTranslations("cms.fields"), serverApi(), cmsLabels("announcements")]);
  const rows = await load(client.GET("/api/admin/announcements"));
  const countries = await load(client.GET("/api/admin/countries"));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <CmsManager
        path="/api/admin/announcements"
        multipart={true}
        labels={labels}
        titleKeys={["countryNameEn"]}
        defaults={{ countryId: String(countries[0]?.id ?? ""), sortOrder: "0", isActive: true }}
        fields={[
          { name: "countryId", label: f("countryId"), kind: "hidden" },
          { name: "sortOrder", label: f("sortOrder"), kind: "number", hint: f("sortOrderHint") },
          { name: "isActive", label: f("isActive"), kind: "bool" },
          { name: "image", label: f("image"), kind: "file", current: "imageFileId" },
        ]}
        columns={[
          { key: "imageFileId", label: f("image"), kind: "file" },
          { key: "countryNameEn", label: f("countryName") },
          { key: "sortOrder", label: f("sortOrder") },
          { key: "isActive", label: f("isActive"), kind: "bool" },
        ]}
        rows={rows}
      />
    </>
  );
}
