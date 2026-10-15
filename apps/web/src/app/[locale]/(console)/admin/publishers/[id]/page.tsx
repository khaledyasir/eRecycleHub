import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getLocale, getTranslations } from "next-intl/server";
import { AccountForm, DeleteButton, FlagSwitch } from "@/components/admin/admin-forms";
import { PageHeader, Panel, Stat } from "@/components/ui/display";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { fileUrl, money, number } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.publishers"))("details") };
}

export default async function Publisher({ params }: { params: Promise<{ id: string }> }) {
  const id = Number((await params).id);
  if (!Number.isInteger(id) || id <= 0) notFound();
  const [t, tu, ts, locale, client] = await Promise.all([getTranslations("admin.publishers"), getTranslations("admin.users"), getTranslations("account.settings"), getLocale(), serverApi()]);
  const [d, countries] = await Promise.all([load(client.GET("/api/admin/publishers/{id}", { params: { path: { id } } })), load(client.GET("/api/countries"))]);
  const p = d.profile;
  const docs = [["crDocument", p.crDocument], ["vatCertificate", p.vatCertificate], ["idDocument", p.idDocument]] as const;
  return (
    <>
      <Link href="/admin/publishers" className="text-sm text-patina hover:underline">{t("title")}</Link>
      <PageHeader
        title={d.publisher.name}
        description={d.publisher.email}
        actions={
          <>
            <span className="inline-flex items-center gap-2 text-sm">{tu("activeLabel")} <FlagSwitch path={`/api/admin/publishers/${id}/active`} field="active" value={d.publisher.isActive} label={tu("activeLabel")} on={tu("activated")} off={tu("deactivated")} /></span>
            <Link href={`/admin/auctions?publisherId=${id}&view=all`} className="inline-flex h-10 items-center rounded-md border border-line-strong bg-surface px-3 text-sm font-medium hover:bg-sunken">{t("viewAuctions")}</Link>
            <Link href={`/stores/${id}`} target="_blank" className="inline-flex h-10 items-center rounded-md border border-line-strong bg-surface px-3 text-sm font-medium hover:bg-sunken">{t("viewStore")}</Link>
            <DeleteButton path={`/api/admin/publishers/${id}`} title={t("deleteTitle")} body={t("deleteBody")} label={t("delete")} done={t("deleted")} redirectTo="/admin/publishers" />
          </>
        }
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Stat label={t("auctions")} value={number(d.publisher.auctionCount, locale)} />
        <Stat label={t("bidsOn")} value={number(d.bidsOnAuctions, locale)} />
        <Stat label={t("bidders")} value={number(d.bidders, locale)} />
        <Stat label={tu("wallet")} value={money(d.walletBalance, locale)} />
      </div>
      <Panel title={tu("documents")} className="mt-4">
        <ul className="grid gap-2 text-sm sm:grid-cols-3">
          {docs.map(([k, f]) => (
            <li key={k}><span className="text-muted">{ts(k)}: </span>{f ? <a href={fileUrl(f.id)} target="_blank" rel="noopener" className="text-patina hover:underline">{f.name}</a> : tu("noDocument")}</li>
          ))}
        </ul>
      </Panel>
      <h2 className="mb-3 mt-8 text-lg font-semibold">{tu("edit")}</h2>
      <div className="max-w-3xl rounded-lg border border-line bg-surface p-5 sm:p-7">
        <AccountForm
          kind="publisher"
          id={id}
          countries={countries}
          initial={{ firstName: p.firstName, lastName: p.lastName, address: p.address, secondMobile: p.secondMobile, email: p.email, phone: p.phone, countryId: p.countryId, isActive: d.publisher.isActive, companyName: p.companyName, crNumber: p.crNumber, vatNumber: p.vatNumber, bankName: p.bankName, accountNumber: p.accountNumber, cardNumber: p.cardNumber, crDocument: p.crDocument, vatCertificate: p.vatCertificate, idDocument: p.idDocument }}
        />
      </div>
    </>
  );
}
