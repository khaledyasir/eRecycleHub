import type { Metadata } from "next";
import { ClipboardList, FileText } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { ApplicationActions } from "@/components/admin/admin-forms";
import { Badge, EmptyState, PageHeader, type Tone } from "@/components/ui/display";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { cn, date, fileUrl, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("admin.applications"))("title") };
}

const tones: Record<string, Tone> = { Pending: "warn", Approved: "ok", Rejected: "danger" };

export default async function Applications({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const status = ["Pending", "Approved", "Rejected"].includes(sp.status ?? "") ? sp.status : sp.status === "all" ? undefined : "Pending";
  const [t, locale, client] = await Promise.all([getTranslations("admin.applications"), getLocale(), serverApi()]);
  const r = await load(client.GET("/api/admin/applications", { params: { query: { status: status as "Pending" | "Approved" | "Rejected" | undefined, page: Math.max(1, Number(sp.page) || 1) } } }));
  const tabs = [["Pending", t("pending")], ["Approved", t("approved")], ["Rejected", t("rejected")], ["all", t("all")]] as const;
  const current = status ?? "all";
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <nav className="mb-4 flex gap-1 overflow-x-auto border-b border-line" aria-label={t("title")}>
        {tabs.map(([v, label]) => (
          <Link key={v} href={`/admin/applications?status=${v}`} aria-current={current === v ? "page" : undefined} className={cn("-mb-px shrink-0 border-b-2 px-3 py-2.5 text-sm font-medium", current === v ? "border-patina text-ink" : "border-transparent text-muted hover:text-ink")}>{label}</Link>
        ))}
      </nav>
      {r.items.length === 0 ? <EmptyState icon={<ClipboardList className="h-8 w-8" />} title={t("empty")} /> : (
        <ul className="space-y-3">
          {r.items.map((a) => (
            <li key={a.id} className="rounded-lg border border-line bg-surface p-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="font-semibold">{a.name} {a.company && <span className="font-normal text-muted">— {a.company}</span>}</p>
                  <p className="text-sm text-ink-2"><a href={`mailto:${a.email}`} className="hover:underline">{a.email}</a>{a.mobile && <span className="ms-3" dir="ltr">{a.mobile}</span>}</p>
                  <p className="mt-1 text-xs text-muted">
                    {pick(locale, a.countryNameEn, a.countryNameAr)}{a.countryNameEn ? " — " : ""}{t("submitted")}: {date(a.createdAtUtc, locale)}{a.decidedAtUtc ? ` — ${t("decided")}: ${date(a.decidedAtUtc, locale)}` : ""}
                  </p>
                  {a.address && <p className="mt-1 text-xs text-muted">{t("address")}: {a.address}</p>}
                  {a.categories.length > 0 && (
                    <p className="mt-2 flex flex-wrap gap-1"><span className="text-xs text-muted">{t("categories")}:</span>{a.categories.map((c) => <Badge key={c.id}>{pick(locale, c.titleEn, c.titleAr)}</Badge>)}</p>
                  )}
                  <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
                    <span className="text-xs text-muted">{t("documents")}:</span>
                    {([[t("cr"), a.crDocument], [t("id"), a.idDocument], [t("vat"), a.vatCertificate]] as const).map(([label, doc]) =>
                      doc ? (
                        <a key={label} href={fileUrl(doc.id)} target="_blank" rel="noopener" className="inline-flex items-center gap-1 font-medium text-patina hover:underline">
                          <FileText className="h-4 w-4" aria-hidden /> {label}
                        </a>
                      ) : null,
                    )}
                    {!a.crDocument && !a.idDocument && !a.vatCertificate && <span className="text-xs text-muted">{t("noDocuments")}</span>}
                  </div>
                  {a.rejectionReason && <p className="mt-2 text-sm text-rust">{t("reasonShown")}: {a.rejectionReason}</p>}
                  <p className="mt-2 text-xs"><Link href={`/admin/users/${a.userId}`} className="text-patina hover:underline">{t("account")} #{a.userId}</Link></p>
                </div>
                <div className="flex flex-col items-end gap-2">
                  <Badge tone={tones[a.status]}>{t(`status.${a.status}`)}</Badge>
                  <ApplicationActions app={a} />
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/admin/applications" query={sp} />
    </>
  );
}
