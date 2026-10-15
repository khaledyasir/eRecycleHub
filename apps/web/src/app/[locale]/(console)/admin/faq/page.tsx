import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { cmsLabels } from "@/components/admin/cms-labels";
import { CmsManager } from "@/components/admin/cms-manager";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";
import { pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.faq"))("title") };
}

export default async function Page() {
  const [t, ts, f, locale, client, labels, sectionLabels] = await Promise.all([
    getTranslations("cms.faq"), getTranslations("cms.faqSections"), getTranslations("cms.fields"), getLocale(), serverApi(), cmsLabels("faq"), cmsLabels("faqSections"),
  ]);
  const [sections, questions] = await Promise.all([load(client.GET("/api/admin/faq-categories")), load(client.GET("/api/admin/faqs"))]);
  const sectionOptions = sections.map((c) => ({ value: c.id, label: pick(locale, c.titleEn, c.titleAr) }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <CmsManager
        path="/api/admin/faqs"
        labels={labels}
        titleKeys={["questionEn", "questionAr"]}
        defaults={{ sortOrder: "0", isActive: true }}
        fields={[
          { name: "categoryId", label: f("categoryId"), kind: "select", required: true, options: sectionOptions },
          { name: "questionEn", label: f("questionEn"), kind: "text", required: true },
          { name: "questionAr", label: f("questionAr"), kind: "text", required: true, rtl: true },
          { name: "answerEn", label: f("answerEn"), kind: "rich" },
          { name: "answerAr", label: f("answerAr"), kind: "rich", rtl: true },
          { name: "sortOrder", label: f("sortOrder"), kind: "number", hint: f("sortOrderHint") },
          { name: "isActive", label: f("isActive"), kind: "bool" },
        ]}
        columns={[
          { key: "questionEn", label: f("questionEn") },
          { key: "questionAr", label: f("questionAr") },
          { key: "categoryId", label: f("categoryId"), kind: "option", options: Object.fromEntries(sectionOptions.map((o) => [String(o.value), o.label])) },
          { key: "sortOrder", label: f("sortOrder") },
          { key: "isActive", label: f("isActive"), kind: "bool" },
        ]}
        rows={questions}
      />
      <h2 className="mb-1 mt-10 text-lg font-semibold">{ts("title")}</h2>
      <div className="mt-3">
        <CmsManager
          path="/api/admin/faq-categories"
          labels={sectionLabels}
          titleKeys={["titleEn", "titleAr"]}
          defaults={{ sortOrder: "0", isActive: true }}
          fields={[
            { name: "titleEn", label: f("titleEn"), kind: "text", required: true },
            { name: "titleAr", label: f("titleAr"), kind: "text", required: true, rtl: true },
            { name: "sortOrder", label: f("sortOrder"), kind: "number", hint: f("sortOrderHint") },
            { name: "isActive", label: f("isActive"), kind: "bool" },
          ]}
          columns={[
            { key: "titleEn", label: f("titleEn") },
            { key: "titleAr", label: f("titleAr") },
            { key: "faqCount", label: f("faqCount") },
            { key: "sortOrder", label: f("sortOrder") },
            { key: "isActive", label: f("isActive"), kind: "bool" },
          ]}
          rows={sections}
        />
      </div>
    </>
  );
}
