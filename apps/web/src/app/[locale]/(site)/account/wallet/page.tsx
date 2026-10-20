import type { Metadata } from "next";
import { Banknote, Clock, Info, Landmark } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import type { ReactNode } from "react";
import { CopyButton } from "@/components/account/copy-button";
import { TopUpForm } from "@/components/account/account-forms";
import { Badge, PageHeader, Panel, type Tone } from "@/components/ui/display";
import { Table, Tabs } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { load, loadOptional, serverApi } from "@/lib/api/server";
import type { Schema } from "@/lib/api/types";
import { cn, date, fileUrl, money, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.wallet"))("title") };
}

const statusTone: Record<string, Tone> = { Pending: "warn", Approved: "ok", Rejected: "danger", Reversed: "neutral" };

/** Payment details come from admin settings "payment-*" (value = the text to show, same in both languages unless an Arabic value is set). */
const PAYMENT_KEYS = ["bank-name", "account-holder", "iban", "swift", "cliq", "processing-time"] as const;

/** A table whose header always shows; when there are no rows it says so inside the table. */
function DataTable({ head, empty, children, count }: { head: string[]; empty: string; children: ReactNode; count: number }) {
  return (
    <Table>
      <thead>
        <tr>{head.map((h) => <th key={h}>{h}</th>)}</tr>
      </thead>
      <tbody>
        {count === 0 ? (
          <tr>
            <td colSpan={head.length} className="py-10 text-center text-muted">{empty}</td>
          </tr>
        ) : (
          children
        )}
      </tbody>
    </Table>
  );
}

export default async function Wallet({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, locale, client] = await Promise.all([getTranslations("account.wallet"), getLocale(), serverApi()]);
  const [w, txs, ledger, ...settings] = await Promise.all([
    load(client.GET("/api/account/wallet")),
    load(client.GET("/api/account/wallet/transactions", { params: { query: { page: Math.max(1, Number(sp.tpage) || 1) } } })),
    load(client.GET("/api/account/wallet/ledger", { params: { query: { page: Math.max(1, Number(sp.lpage) || 1) } } })),
    ...PAYMENT_KEYS.map((key) => loadOptional<Schema<"SettingDto">>(client.GET("/api/content/settings/{key}", { params: { path: { key: `payment-${key}` } } }))),
  ]);
  const pay = Object.fromEntries(PAYMENT_KEYS.map((k, i) => [k, pick(locale, settings[i]?.valueEn, settings[i]?.valueAr).trim()])) as Record<(typeof PAYMENT_KEYS)[number], string>;
  const details = [
    [t("bankName"), pay["bank-name"], false],
    [t("accountHolder"), pay["account-holder"], false],
    [t("iban"), pay.iban, true],
    [t("swift"), pay.swift, true],
    [t("cliq"), pay.cliq, true],
  ] as const;
  const shown = details.filter(([, value]) => value);

  return (
    <>
      <PageHeader title={t("title")} />
      <div className="grid items-start gap-4 lg:grid-cols-[1fr_1.2fr]">
        <div className="space-y-4">
        <section className="rounded-xl border border-line bg-surface p-5">
          <p className="text-sm text-muted">{t("balance")}</p>
          <p className="tabular mt-1 text-4xl font-black text-patina">{money(w.balance, locale)}</p>
          <div className="mt-5 flex items-center gap-3 rounded-lg bg-sunken p-3">
            <span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-orange/10 text-orange">
              <Clock className="h-4 w-4" aria-hidden />
            </span>
            <div className="min-w-0 text-sm">
              <p className="text-xs text-muted">{t("pendingTitle")}</p>
              <p className="tabular font-bold">{money(w.pendingAmount, locale)} <span className="text-xs font-normal text-muted">· {t("pendingCount", { count: w.pendingRequests })}</span></p>
            </div>
          </div>
          <div className="mt-5 flex items-start gap-2.5 rounded-lg border border-patina-100 bg-patina-50 p-3 text-sm text-ink-2">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-patina" aria-hidden />
            <p>{t("explain")}</p>
          </div>
        </section>
          {shown.length > 0 && (
            <div className="overflow-hidden rounded-xl border border-line bg-surface">
              <p className="flex items-center gap-2 border-b border-line bg-sunken/60 px-4 py-2.5 text-sm font-bold">
                <Landmark className="h-4 w-4 text-patina" aria-hidden /> {t("bankTitle")}
              </p>
              <dl className="divide-y divide-line">
                {shown.map(([label, value, copyable]) => (
                  <div key={label} className="flex items-center justify-between gap-3 px-4 py-2.5 text-sm">
                    <div className="min-w-0">
                      <dt className="text-xs text-muted">{label}</dt>
                      <dd className={cn("break-all font-medium", copyable && "tabular")} dir={copyable ? "ltr" : undefined}>{value}</dd>
                    </div>
                    {copyable && <CopyButton value={value} />}
                  </div>
                ))}
              </dl>
            </div>
          )}
        </div>

        <Panel title={t("topUpTitle")}>
          <p className="mb-3 text-sm text-muted">{t("topUpBody")}</p>
          <p className="mb-4 flex items-start gap-2 rounded-lg bg-sunken p-3 text-xs text-ink-2">
            <Banknote className="mt-0.5 h-4 w-4 shrink-0 text-patina" aria-hidden />
            <span>{pay["processing-time"] ? t("processing", { time: pay["processing-time"] }) : t("processingDefault")}</span>
          </p>
          <TopUpForm max={w.maxTopUp} />
        </Panel>
      </div>

      <div className="mt-8">
        <Tabs
          defaultValue={sp.tab === "requests" ? "requests" : "history"}
          items={[
            {
              value: "history",
              label: t("tabHistory"),
              content: (
                <>
                  <DataTable head={[t("date"), t("reference"), t("type"), t("change"), t("balanceAfter")]} empty={t("ledgerEmpty")} count={ledger.items.length}>
                    {ledger.items.map((l) => (
                      <tr key={l.id}>
                        <td className="text-muted">{date(l.createdAtUtc, locale)}</td>
                        <td className="tabular text-muted" dir="ltr">#{l.id}</td>
                        <td>{t(`reasonLabel.${l.reason}`)}</td>
                        <td className={cn("tabular font-medium", l.amount < 0 ? "text-rust" : "text-ok")} dir="ltr">{l.amount > 0 ? "+" : ""}{money(l.amount, locale)}</td>
                        <td className="tabular">{money(l.balanceAfter, locale)}</td>
                      </tr>
                    ))}
                  </DataTable>
                  <Pagination page={ledger.page} totalPages={Math.ceil(ledger.total / ledger.pageSize)} pathname="/account/wallet" query={{ ...sp, tab: "history" }} param="lpage" />
                </>
              ),
            },
            {
              value: "requests",
              label: t("tabRequests"),
              content: (
                <>
                  <DataTable head={[t("date"), t("reference"), t("amount"), t("status"), t("receipt")]} empty={t("requestsEmpty")} count={txs.items.length}>
                    {txs.items.map((x) => (
                      <tr key={x.id}>
                        <td className="text-muted">{date(x.createdAtUtc, locale)}</td>
                        <td className="tabular text-muted" dir="ltr">#{x.id}</td>
                        <td className="tabular font-medium">{money(x.amount, locale)}</td>
                        <td><Badge tone={statusTone[x.status]}>{t(`statusLabel.${x.status}`)}</Badge></td>
                        <td>{x.proofFileId ? <a href={fileUrl(x.proofFileId)} target="_blank" rel="noopener" className="text-patina hover:underline">{t("view")}</a> : <span className="text-muted">—</span>}</td>
                      </tr>
                    ))}
                  </DataTable>
                  <Pagination page={txs.page} totalPages={Math.ceil(txs.total / txs.pageSize)} pathname="/account/wallet" query={{ ...sp, tab: "requests" }} param="tpage" />
                </>
              ),
            },
          ]}
        />
      </div>
    </>
  );
}
