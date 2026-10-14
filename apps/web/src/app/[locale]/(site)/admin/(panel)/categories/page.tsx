import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";
import { pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.categories"))("title") };
}

export default async function Page() {
  const [t, f, locale, client, labels] = await Promise.all([getTranslations("cms.categories"), getTranslations("cms.fields"), getLocale(), serverApi(), cmsLabels("categories")]);
  const rows = await load(client.GET("/api/admin/categories"));
  const countries = await load(client.GET("/api/admin/countries"));
  const countryOptions = countries.map((c) => ({ value: c.id, label: pick(locale, c.nameEn, c.nameAr) }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <CmsManager
        path="/api/admin/categories"
        multipart={true}
        labels={labels}
        titleKeys={["titleEn", "titleAr"]}
        defaults={{ sortOrder: "0", isActive: true }}
        fields={[
          { name: "countryId", label: f("countryId"), kind: "select", required: true, options: countryOptions },
          { name: "titleEn", label: f("titleEn"), kind: "text", required: true },
          { name: "titleAr", label: f("titleAr"), kind: "text", required: true, rtl: true },
          { name: "sortOrder", label: f("sortOrder"), kind: "number", hint: f("sortOrderHint") },
          { name: "isActive", label: f("isActive"), kind: "bool" },
          { name: "icon", label: f("icon"), kind: "file", current: "iconFileId" },
          { name: "image", label: f("image"), kind: "file", current: "imageFileId" },
        ]}
        columns={[
          { key: "iconFileId", label: f("icon"), kind: "file" },
          { key: "titleEn", label: f("titleEn") },
          { key: "titleAr", label: f("titleAr") },
          { key: "countryNameEn", label: f("countryName") },
          { key: "sortOrder", label: f("sortOrder") },
          { key: "isActive", label: f("isActive"), kind: "bool" },
          { key: "auctionCount", label: f("auctions") },
        ]}
        rows={rows}
      />
    </>
  );
}
