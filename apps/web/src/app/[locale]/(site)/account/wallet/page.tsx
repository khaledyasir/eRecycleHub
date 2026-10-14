import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { TopUpForm } from "@/components/account/account-forms";
import { Badge, EmptyState, PageHeader, Panel, type Tone } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { load, serverApi } from "@/lib/api/server";
import { cn, date, fileUrl, money } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.wallet"))("title") };
}

const statusTone: Record<string, Tone> = { Pending: "warn", Approved: "ok", Rejected: "danger", Reversed: "neutral" };

export default async function Wallet({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, locale, client] = await Promise.all([getTranslations("account.wallet"), getLocale(), serverApi()]);
  const [w, txs, ledger] = await Promise.all([
    load(client.GET("/api/account/wallet")),
    load(client.GET("/api/account/wallet/transactions", { params: { query: { page: Math.max(1, Number(sp.tpage) || 1) } } })),
    load(client.GET("/api/account/wallet/ledger", { params: { query: { page: Math.max(1, Number(sp.lpage) || 1) } } })),
  ]);
  return (
    <>
      <PageHeader title={t("title")} />
      <div className="grid gap-4 lg:grid-cols-[1fr_1.2fr]">
        <section className="rounded-lg border border-line bg-surface p-5">
          <p className="text-sm text-muted">{t("balance")}</p>
          <p className="tabular mt-1 text-3xl font-semibold">{money(w.balance, locale)}</p>
          <p className="mt-2 text-sm text-ink-2">{t("pending", { count: w.pendingRequests, amount: money(w.pendingAmount, locale) })}</p>
          <p className="mt-4 text-sm text-muted">{t("explain")}</p>
        </section>
        <Panel title={t("topUpTitle")}>
          <p className="mb-4 text-sm text-muted">{t("topUpBody")}</p>
          <TopUpForm max={w.maxTopUp} />
        </Panel>
      </div>

      <h2 className="mb-3 mt-8 text-lg font-semibold">{t("requests")}</h2>
      {txs.items.length === 0 ? (
        <EmptyState title={t("requestsEmpty")} />
      ) : (
        <Table>
          <thead><tr><th>{t("date")}</th><th>{t("amount")}</th><th>{t("status")}</th><th>{t("receipt")}</th></tr></thead>
          <tbody>
            {txs.items.map((x) => (
              <tr key={x.id}>
                <td className="text-muted">{date(x.createdAtUtc, locale)}</td>
                <td className="tabular font-medium">{money(x.amount, locale)}</td>
                <td><Badge tone={statusTone[x.status]}>{t(`statusLabel.${x.status}`)}</Badge></td>
                <td>{x.proofFileId ? <a href={fileUrl(x.proofFileId)} target="_blank" rel="noopener" className="text-patina hover:underline">{t("view")}</a> : <span className="text-muted">—</span>}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={txs.page} totalPages={Math.ceil(txs.total / txs.pageSize)} pathname="/account/wallet" query={sp} param="tpage" />

      <h2 className="mb-3 mt-8 text-lg font-semibold">{t("ledger")}</h2>
      {ledger.items.length === 0 ? (
        <EmptyState title={t("ledgerEmpty")} />
      ) : (
        <Table>
          <thead><tr><th>{t("date")}</th><th>{t("reason")}</th><th>{t("change")}</th><th>{t("balanceAfter")}</th></tr></thead>
          <tbody>
            {ledger.items.map((l) => (
              <tr key={l.id}>
                <td className="text-muted">{date(l.createdAtUtc, locale)}</td>
                <td>{t(`reasonLabel.${l.reason}`)}</td>
                <td className={cn("tabular font-medium", l.amount < 0 ? "text-rust" : "text-ok")} dir="ltr">{l.amount > 0 ? "+" : ""}{money(l.amount, locale)}</td>
                <td className="tabular">{money(l.balanceAfter, locale)}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={ledger.page} totalPages={Math.ceil(ledger.total / ledger.pageSize)} pathname="/account/wallet" query={sp} param="lpage" />
    </>
  );
}
