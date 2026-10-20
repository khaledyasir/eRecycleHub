import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { ProfileForm } from "@/components/account/account-forms";
import { KeyRound } from "lucide-react";
import { PageHeader } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.settings"))("title") };
}

export default async function Settings() {
  const [t, client] = await Promise.all([getTranslations("account.settings"), serverApi()]);
  const profile = await load(client.GET("/api/account/profile"));
  return (
    <>
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <Link href="/account/password" className="inline-flex h-10 items-center gap-2 rounded-md border border-line-strong bg-surface px-4 text-sm font-bold hover:bg-sunken">
            <KeyRound className="h-4 w-4 text-patina" aria-hidden /> {t("changePassword")}
          </Link>
        }
      />
      <div className="max-w-3xl rounded-lg border border-line bg-surface p-5 sm:p-7">
        <ProfileForm profile={profile} />
      </div>
    </>
  );
}
