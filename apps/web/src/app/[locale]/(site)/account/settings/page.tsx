import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { ProfileForm } from "@/components/account/account-forms";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.settings"))("title") };
}

export default async function Settings() {
  const [t, client] = await Promise.all([getTranslations("account.settings"), serverApi()]);
  const [profile, countries] = await Promise.all([load(client.GET("/api/account/profile")), load(client.GET("/api/countries"))]);
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <div className="max-w-3xl rounded-lg border border-line bg-surface p-5 sm:p-7">
        <ProfileForm profile={profile} countries={countries} />
      </div>
    </>
  );
}
