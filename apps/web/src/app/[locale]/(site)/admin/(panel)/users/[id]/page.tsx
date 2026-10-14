import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getLocale, getTranslations } from "next-intl/server";
import { AccountForm } from "@/components/admin/admin-forms";
import { Badge, PageHeader, Panel, Stat } from "@/components/ui/display";
import { Table, Tabs } from "@/components/ui/controls";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { cn, date, dateTime, fileUrl, money, number, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.users"))("details") };
}

export default async function Customer({ params }: { params: Promise<{ id: string }> }) {
  const id = Number((await params).id);
  if (!Number.isInteger(id) || id <= 0) notFound();
  const [t, tw, tb, locale, client] = await Promise.all([getTranslations("admin.users"), getTranslations("account.wallet"), getTranslations("account.dashboard"), getLocale(), serverApi()]);
  const [u, txs, ledger, countries] = await Promise.all([
    load(client.GET("/api/admin/users/{id}", { params: { path: { id } } })),
    load(client.GET("/api/admin/users/{id}/transactions", { params: { path: { id } } })),
    load(client.GET("/api/admin/users/{id}/ledger", { params: { path: { id } } })),
    load(client.GET("/api/countries")),
  ]);
  const p = u.profile;
  const docs = [["crDocument", p.crDocument], ["vatCertificate", p.vatCertificate], ["idDocument", p.idDocument]] as const;
  const ts = await getTranslations("account.settings");
  return (
    <>
      <Link href="/admin/users" className="text-sm text-patina hover:underline">{t("title")}</Link>
      <PageHeader
        title={p.fullName}
        description={p.email}
        actions={<><Badge tone={u.isActive ? "ok" : "danger"}>{u.isActive ? t("active") : t("archived")}</Badge><Link href={`/admin/transactions/new?userId=${p.id}&name=${encodeURIComponent(p.fullName)}`} className="inline-flex h-10 items-center rounded-md bg-patina px-4 text-sm font-medium text-white hover:bg-patina-700">{t("addCredit")}</Link></>}
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Stat label={t("wallet")} value={money(p.walletBalance, locale)} hint={t("walletReadOnly")} />
        <Stat label={t("bids")} value={number(u.bidCount, locale)} />
        <Stat label={t("won")} value={number(u.won, locale)} />
        <Stat label={t("lastLogin")} value={<span className="text-base">{u.lastLoginAtUtc ? dateTime(u.lastLoginAtUtc, locale) : t("never")}</span>} hint={`${t("joined")}: ${date(u.createdAtUtc, locale)}`} />
      </div>
      <Panel title={t("documents")} className="mt-4">
        <ul className="grid gap-2 text-sm sm:grid-cols-3">
          {docs.map(([k, f]) => (
            <li key={k}><span className="text-muted">{ts(k)}: </span>{f ? <a href={fileUrl(f.id)} target="_blank" rel="noopener" className="text-patina hover:underline">{f.name}</a> : t("noDocument")}</li>
          ))}
        </ul>
      </Panel>
      <div className="mt-6">
        <Tabs
          items={[
            { value: "edit", label: t("edit"), content: <div className="max-w-3xl rounded-lg border border-line bg-surface p-5 sm:p-7"><AccountForm kind="user" id={p.id} countries={countries} initial={{ ...p, isActive: u.isActive }} /></div> },
            { value: "bids", label: t("recentBids"), content: (
              <Table>
                <tbody>
                  {u.recentBids.map((b) => (
                    <tr key={b.id}>
                      <td><Link href={`/admin/auctions/${b.auctionId}`} className="font-medium hover:underline">{pick(locale, b.titleEn, b.titleAr)}</Link></td>
                      <td className="tabular">{money(b.amount, locale)}</td>
                      <td>{tb(`statusLabel.${b.status}`)}</td>
                      <td className="text-muted">{dateTime(b.createdAtUtc, locale)}</td>
                    </tr>
                  ))}
                </tbody>
              </Table>
            ) },
            { value: "tx", label: t("transactions"), content: (
              <Table>
                <tbody>
                  {txs.items.map((x) => (
                    <tr key={x.id}>
                      <td className="text-muted">{date(x.createdAtUtc, locale)}</td>
                      <td className="tabular">{money(x.amount, locale)}</td>
                      <td>{tw(`statusLabel.${x.status}`)}</td>
                      <td>{x.proofFileId ? <a href={fileUrl(x.proofFileId)} target="_blank" rel="noopener" className="text-patina hover:underline">{tw("view")}</a> : "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </Table>
            ) },
            { value: "ledger", label: t("ledger"), content: (
              <Table>
                <tbody>
                  {ledger.items.map((l) => (
                    <tr key={l.id}>
                      <td className="text-muted">{date(l.createdAtUtc, locale)}</td>
                      <td>{tw(`reasonLabel.${l.reason}`)}</td>
                      <td className={cn("tabular", l.amount < 0 ? "text-rust" : "text-ok")} dir="ltr">{money(l.amount, locale)}</td>
                      <td className="tabular">{money(l.balanceAfter, locale)}</td>
                    </tr>
                  ))}
                </tbody>
              </Table>
            ) },
          ]}
        />
      </div>
    </>
  );
}
