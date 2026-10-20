import type { Metadata } from "next";
import { CheckCircle2, Clock, Store, XCircle } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { SellerApplicationForm } from "@/components/account/seller-application";
import { PageHeader } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { date } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.seller"))("title") };
}

export default async function BecomeSeller() {
  const [t, locale, client] = await Promise.all([getTranslations("account.seller"), getLocale(), serverApi()]);
  const [status, profile] = await Promise.all([load(client.GET("/api/account/seller-application")), load(client.GET("/api/account/profile"))]);
  const app = status.application;
  const categories = await load(client.GET("/api/categories", { params: { query: { countryId: profile.countryId } } }));

  return (
    <>
      <PageHeader title={t("title")} description={t("intro")} />

      {status.isSeller && (
        <div className="max-w-3xl rounded-xl border border-ok/30 bg-ok-soft p-6">
          <p className="flex items-center gap-2 text-lg font-bold text-ok">
            <CheckCircle2 className="h-5 w-5" aria-hidden /> {t("isSellerTitle")}
          </p>
          <p className="mt-2 text-ink-2">{t("isSellerBody")}</p>
          <Link href="/seller" className="mt-4 inline-flex h-11 items-center gap-2 rounded-md bg-patina px-5 text-sm font-bold text-white hover:bg-patina-700">
            <Store className="h-4 w-4" aria-hidden /> {t("openPortal")}
          </Link>
        </div>
      )}

      {!status.isSeller && app?.status === "Pending" && (
        <div className="max-w-3xl rounded-xl border border-hivis bg-hivis-soft p-6">
          <p className="flex items-center gap-2 text-lg font-bold text-hivis-ink">
            <Clock className="h-5 w-5" aria-hidden /> {t("pendingTitle")}
          </p>
          <p className="mt-2 text-ink-2">{t("pendingBody")}</p>
          <p className="mt-3 text-xs text-muted">
            {t("submitted")}: {date(app.createdAtUtc, locale)}
          </p>
        </div>
      )}

      {status.canApply && (
        <div className="max-w-3xl space-y-5">
          {app?.status === "Rejected" && (
            <div className="rounded-xl border border-rust/30 bg-rust-soft p-5">
              <p className="flex items-center gap-2 font-bold text-rust">
                <XCircle className="h-5 w-5" aria-hidden /> {t("rejectedTitle")}
              </p>
              {app.rejectionReason && (
                <p className="mt-2 text-sm">
                  <span className="font-semibold">{t("reason")}:</span> {app.rejectionReason}
                </p>
              )}
              <p className="mt-2 text-sm text-ink-2">{t("reapply")}</p>
            </div>
          )}
          <div className="rounded-xl border border-line bg-surface p-5 shadow-raise sm:p-7">
            <SellerApplicationForm
              categories={categories}
              defaults={{ name: app?.name ?? profile.companyName ?? profile.fullName, company: app?.company ?? profile.companyName ?? "", mobile: app?.mobile ?? profile.phone ?? "", address: app?.address ?? "", crNumber: profile.crNumber ?? "", vatNumber: profile.vatNumber ?? "", bankName: profile.bankName ?? "", accountNumber: profile.accountNumber ?? "", cardNumber: profile.cardNumber ?? "" }}
              onFile={{ cr: profile.crDocument?.name, vat: profile.vatCertificate?.name, id: profile.idDocument?.name }}
            />
          </div>
        </div>
      )}
    </>
  );
}
