import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { PasswordForm } from "@/components/account/account-forms";
import { PageHeader } from "@/components/ui/display";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("seller.password"))("title") };
}

export default async function SellerPassword() {
  const t = await getTranslations("seller.password");
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <div className="max-w-xl rounded-lg border border-line bg-surface p-5 sm:p-7">
        <PasswordForm ns="seller.password" />
      </div>
    </>
  );
}
