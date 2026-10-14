import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";
import { pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.announcements"))("title") };
}

export default async function Page() {
  const [t, f, locale, client, labels] = await Promise.all([getTranslations("cms.announcements"), getTranslations("cms.fields"), getLocale(), serverApi(), cmsLabels("announcements")]);
  const rows = await load(client.GET("/api/admin/announcements"));
  const countries = await load(client.GET("/api/admin/countries"));
  const countryOptions = countries.map((c) => ({ value: c.id, label: pick(locale, c.nameEn, c.nameAr) }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <CmsManager
        path="/api/admin/announcements"
        multipart={true}
        labels={labels}
        titleKeys={["countryNameEn"]}
        defaults={{ sortOrder: "0", isActive: true }}
        fields={[
          { name: "countryId", label: f("countryId"), kind: "select", required: true, options: countryOptions },
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
