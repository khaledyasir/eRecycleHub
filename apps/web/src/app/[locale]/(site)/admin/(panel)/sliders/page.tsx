import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.sliders"))("title") };
}

export default async function Page() {
  const [t, f, client, labels] = await Promise.all([getTranslations("cms.sliders"), getTranslations("cms.fields"), serverApi(), cmsLabels("sliders")]);
  const rows = await load(client.GET("/api/admin/sliders"));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <CmsManager
        path="/api/admin/sliders"
        multipart={true}
        labels={labels}
        titleKeys={["title"]}
        defaults={{ type: "Web", sortOrder: "0", isActive: true }}
        fields={[
          { name: "title", label: f("title"), kind: "text" },
          { name: "link", label: f("link"), kind: "url" },
          { name: "type", label: f("type"), kind: "select", required: true, options: [{ value: "Web", label: f("typeWeb") }, { value: "Mobile", label: f("typeMobile") }] },
          { name: "sortOrder", label: f("sortOrder"), kind: "number", hint: f("sortOrderHint") },
          { name: "isActive", label: f("isActive"), kind: "bool" },
          { name: "image", label: f("image"), kind: "file", current: "imageFileId" },
        ]}
        columns={[
          { key: "imageFileId", label: f("image"), kind: "file" },
          { key: "title", label: f("title") },
          { key: "type", label: f("type"), kind: "option", options: { Web: f("typeWeb"), Mobile: f("typeMobile") } },
          { key: "sortOrder", label: f("sortOrder") },
          { key: "isActive", label: f("isActive"), kind: "bool" },
        ]}
        rows={rows}
      />
    </>
  );
}
