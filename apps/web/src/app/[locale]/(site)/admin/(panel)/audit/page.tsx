import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { load, serverApi } from "@/lib/api/server";
import { dateTime } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.audit"))("title") };
}

export default async function Audit({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, locale, client] = await Promise.all([getTranslations("admin.audit"), getLocale(), serverApi()]);
  const r = await load(client.GET("/api/admin/audit", { params: { query: { action: sp.action, page: Math.max(1, Number(sp.page) || 1) } } }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <form className="mb-4 flex flex-wrap items-end gap-2">
        <label className="flex flex-col gap-1 text-xs text-muted">{t("filterAction")}<input name="action" defaultValue={sp.action} placeholder="wallet." className="h-9 rounded-md border border-line-strong bg-surface px-2 text-sm" dir="ltr" /></label>
        <button className="h-9 rounded-md bg-ink px-3 text-sm font-medium text-white">{t("filter")}</button>
      </form>
      {r.items.length === 0 ? <EmptyState title={t("empty")} /> : (
        <Table>
          <thead><tr><th>{t("time")}</th><th>{t("actor")}</th><th>{t("action")}</th><th>{t("entity")}</th><th>{t("ip")}</th><th>{t("data")}</th></tr></thead>
          <tbody>
            {r.items.map((a) => (
              <tr key={a.id}>
                <td className="whitespace-nowrap text-muted">{dateTime(a.createdAtUtc, locale)}</td>
                <td>{a.actorName ?? t("system")}</td>
                <td className="font-mono text-xs" dir="ltr">{a.action}</td>
                <td className="text-xs" dir="ltr">{a.entityType} #{a.entityId}</td>
                <td className="text-xs text-muted" dir="ltr">{a.ipAddress ?? "—"}</td>
                <td>{a.data ? <details><summary className="cursor-pointer text-xs text-patina">{t("data")}</summary><pre className="mt-1 max-w-sm overflow-x-auto whitespace-pre-wrap break-all rounded bg-sunken p-2 text-xs" dir="ltr">{JSON.stringify(JSON.parse(a.data), null, 2)}</pre></details> : "—"}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/admin/audit" query={sp} />
    </>
  );
}
