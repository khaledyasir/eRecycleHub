import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { MessageActions } from "@/components/admin/cms-extras";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { cn, dateTime } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("cms.messages"))("title") };
}

export default async function Messages({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const unread = sp.unread === "true";
  const [t, locale, client] = await Promise.all([getTranslations("cms.messages"), getLocale(), serverApi()]);
  const r = await load(client.GET("/api/admin/messages", { params: { query: { unread: unread || undefined, page: Math.max(1, Number(sp.page) || 1) } } }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <nav className="mb-4 flex gap-1 border-b border-line" aria-label={t("title")}>
        {[false, true].map((u) => (
          <Link key={String(u)} href={u ? "/admin/messages?unread=true" : "/admin/messages"} aria-current={unread === u ? "page" : undefined} className={cn("-mb-px border-b-2 px-3 py-2.5 text-sm font-medium", unread === u ? "border-patina text-ink" : "border-transparent text-muted hover:text-ink")}>
            {u ? t("unread") : t("all")}
          </Link>
        ))}
      </nav>
      {r.items.length === 0 ? (
        <EmptyState title={t("empty")} />
      ) : (
        <ul className="space-y-3">
          {r.items.map((m) => (
            <li key={m.id} className={cn("rounded-lg border bg-surface p-4", m.isRead ? "border-line" : "border-patina/50")}>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="font-semibold">{m.name}</p>
                  <p className="text-sm text-ink-2">
                    <a href={`mailto:${m.email}`} className="hover:underline">{m.email}</a>
                    {m.mobile && <span className="ms-3" dir="ltr">{m.mobile}</span>}
                  </p>
                  <p className="text-xs text-muted">{t("received")}: {dateTime(m.createdAtUtc, locale)}</p>
                </div>
                <MessageActions id={m.id} isRead={m.isRead} />
              </div>
              <p className="mt-3 whitespace-pre-wrap break-words text-sm">{m.message}</p>
            </li>
          ))}
        </ul>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/admin/messages" query={sp} />
    </>
  );
}
