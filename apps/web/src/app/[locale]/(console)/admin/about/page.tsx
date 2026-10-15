import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.about"))("title") };
}

export default async function Page() {
  const [t, f, client, labels] = await Promise.all([getTranslations("cms.about"), getTranslations("cms.fields"), serverApi(), cmsLabels("about")]);
  const rows = await load(client.GET("/api/admin/about"));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <CmsManager
        path="/api/admin/about"
        multipart={true}
        single
        labels={labels}
        titleKeys={["titleEn", "titleAr"]}
        defaults={{}}
        fields={[
          { name: "titleEn", label: f("titleEn"), kind: "text" },
          { name: "titleAr", label: f("titleAr"), kind: "text", rtl: true },
          { name: "descEn", label: f("descEn"), kind: "rich" },
          { name: "descAr", label: f("descAr"), kind: "rich", rtl: true },
          { name: "totalBidders", label: f("totalBidders"), kind: "number" },
          { name: "guidelinesVideoUrl", label: f("guidelinesVideoUrl"), kind: "url" },
          { name: "firstImage", label: f("firstImage"), kind: "file", current: "firstImageFileId" },
          { name: "secondImage", label: f("secondImage"), kind: "file", current: "secondImageFileId" },
        ]}
        columns={[
          { key: "firstImageFileId", label: f("firstImage"), kind: "file" },
          { key: "titleEn", label: f("titleEn") },
          { key: "titleAr", label: f("titleAr") },
          { key: "totalBidders", label: f("totalBidders") },
        ]}
        rows={rows}
      />
    </>
  );
}
