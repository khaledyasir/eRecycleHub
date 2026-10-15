import type { Metadata } from "next";
import { Plus, Wallet } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { TransactionActions } from "@/components/admin/admin-forms";
import { Badge, EmptyState, PageHeader, type Tone } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { date, fileUrl, fromLocalInput, money } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.transactions"))("title") };
}

const tones: Record<string, Tone> = { Pending: "warn", Approved: "ok", Rejected: "danger", Reversed: "neutral" };

export default async function Transactions({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, locale, client] = await Promise.all([getTranslations("admin.transactions"), getLocale(), serverApi()]);
  const status = ["Pending", "Approved", "Rejected", "Reversed"].includes(sp.status ?? "") ? (sp.status as "Pending") : undefined;
  const r = await load(client.GET("/api/admin/transactions", { params: { query: {
    status, q: sp.q, from: sp.from ? fromLocalInput(`${sp.from}T00:00`) : undefined, to: sp.to ? fromLocalInput(`${sp.to}T23:59`) : undefined,
    userId: sp.userId ? Number(sp.userId) : undefined, page: Math.max(1, Number(sp.page) || 1),
  } } }));
  const input = "h-9 rounded-md border border-line-strong bg-surface px-2 text-sm";
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} actions={<Link href="/admin/transactions/new" className="inline-flex h-10 items-center gap-2 rounded-md bg-patina px-4 text-sm font-medium text-white hover:bg-patina-700"><Plus className="h-4 w-4" aria-hidden /> {t("new")}</Link>} />
      <form className="mb-4 flex flex-wrap items-end gap-2">
        <label className="flex flex-col gap-1 text-xs text-muted">{t("status")}
          <select name="status" defaultValue={status ?? ""} className={input}>
            <option value="">{t("statusAny")}</option>
            {(["Pending", "Approved", "Rejected", "Reversed"] as const).map((s) => <option key={s} value={s}>{t(`statusLabel.${s}`)}</option>)}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-xs text-muted">{t("search")}<input name="q" type="search" defaultValue={sp.q} className={input} /></label>
        <label className="flex flex-col gap-1 text-xs text-muted">{t("from")}<input type="date" name="from" defaultValue={sp.from} className={input} /></label>
        <label className="flex flex-col gap-1 text-xs text-muted">{t("to")}<input type="date" name="to" defaultValue={sp.to} className={input} /></label>
        <button className="h-9 rounded-md bg-patina px-4 text-sm font-bold text-white hover:bg-patina-700">{t("filter")}</button>
      </form>
      {r.items.length === 0 ? <EmptyState icon={<Wallet className="h-8 w-8" />} title={t("empty")} /> : (
        <Table>
          <thead><tr><th>{t("id")}</th><th>{t("date")}</th><th>{t("user")}</th><th>{t("amount")}</th><th>{t("status")}</th><th>{t("proof")}</th><th>{t("note")}</th><th><span className="sr-only">{t("approve")}</span></th></tr></thead>
          <tbody>
            {r.items.map((x) => (
              <tr key={x.id}>
                <td className="tabular text-muted">{x.id}</td>
                <td className="whitespace-nowrap text-muted">{date(x.createdAtUtc, locale)}</td>
                <td><Link href={`/admin/users/${x.userId}`} className="font-medium hover:underline">{x.userName}</Link><span className="block text-xs text-muted">{x.userEmail}</span></td>
                <td className="tabular font-semibold">{money(x.amount, locale)}</td>
                <td><Badge tone={tones[x.status]}>{t(`statusLabel.${x.status}`)}</Badge></td>
                <td>{x.proofFileId ? <a href={fileUrl(x.proofFileId)} target="_blank" rel="noopener" className="text-patina hover:underline">{t("view")}</a> : <span className="text-muted">{t("none")}</span>}</td>
                <td className="max-w-48 truncate text-ink-2" title={x.note ?? ""}>{x.note ?? "—"}</td>
                <td><TransactionActions tx={x} /></td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/admin/transactions" query={sp} />
    </>
  );
}
