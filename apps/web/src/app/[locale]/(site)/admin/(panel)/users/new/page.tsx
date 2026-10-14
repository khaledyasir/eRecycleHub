import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AccountForm } from "@/components/admin/admin-forms";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.users"))("new") };
}

export default async function NewCustomer() {
  const [t, client] = await Promise.all([getTranslations("admin.users"), serverApi()]);
  const countries = await load(client.GET("/api/countries"));
  return (
    <>
      <PageHeader title={t("new")} />
      <div className="max-w-3xl rounded-lg border border-line bg-surface p-5 sm:p-7"><AccountForm kind="user" countries={countries} /></div>
    </>
  );
}
