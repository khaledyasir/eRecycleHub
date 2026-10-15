import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { CreditForm } from "@/components/admin/admin-forms";
import { PageHeader } from "@/components/ui/display";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.transactions"))("newTitle") };
}

export default async function NewCredit({ searchParams }: { searchParams: Promise<{ userId?: string; name?: string }> }) {
  const sp = await searchParams;
  const t = await getTranslations("admin.transactions");
  const id = Number(sp.userId);
  return (
    <>
      <PageHeader title={t("newTitle")} description={t("newDescription")} />
      <div className="rounded-lg border border-line bg-surface p-5 sm:p-7">
        <CreditForm defaultUser={Number.isInteger(id) && id > 0 ? { id, name: sp.name ?? `#${id}` } : undefined} />
      </div>
    </>
  );
}
