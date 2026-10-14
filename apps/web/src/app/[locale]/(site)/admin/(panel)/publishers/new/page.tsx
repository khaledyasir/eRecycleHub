import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AccountForm } from "@/components/admin/admin-forms";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.publishers"))("new") };
}

export default async function NewPublisher() {
  const [t, client] = await Promise.all([getTranslations("admin.publishers"), serverApi()]);
  const countries = await load(client.GET("/api/countries"));
  return (
    <>
      <PageHeader title={t("new")} />
      <div className="max-w-3xl rounded-lg border border-line bg-surface p-5 sm:p-7"><AccountForm kind="publisher" countries={countries} /></div>
    </>
  );
}
