import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { StoreProfileForm } from "@/components/seller/seller-forms";
import { PageHeader } from "@/components/ui/display";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("seller.profile"))("title") };
}

export default async function SellerProfile() {
  const [t, client] = await Promise.all([getTranslations("seller.profile"), serverApi()]);
  const [profile, countries] = await Promise.all([load(client.GET("/api/seller/profile")), load(client.GET("/api/countries"))]);
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <div className="max-w-3xl rounded-lg border border-line bg-surface p-5 sm:p-7">
        <StoreProfileForm profile={profile} countries={countries} />
      </div>
    </>
  );
}
