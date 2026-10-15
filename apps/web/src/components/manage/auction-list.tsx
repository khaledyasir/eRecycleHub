import { Gavel, Plus } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { PhaseBadge } from "@/components/auction/auction-card";
import { Badge, EmptyState, PageHeader } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { cn, date, fileUrl, fromLocalInput, money, pick } from "@/lib/format";
import { ActiveSwitch, RowActions } from "./manage-actions";

type Search = Record<string, string | undefined>;

/** Auction management list shared by the seller portal and the admin panel. Filters are a GET form (bookmarkable, no JS required). */
export async function AuctionList({ admin, sp }: { admin: boolean; sp: Search }) {
  const [t, tl, locale, client] = await Promise.all([getTranslations("seller.manage"), getTranslations("auction.list"), getLocale(), serverApi()]);
  const base = admin ? ("/api/admin" as const) : ("/api/seller" as const);
  const routeBase = admin ? "/admin/auctions" : "/seller/auctions";
  const view = ["active", "archived", "all"].includes(sp.view ?? "") ? sp.view! : "active";
  const status = ["live", "upcoming", "ended"].includes(sp.status ?? "") ? sp.status : undefined;
  const query = {
    view, status, q: sp.q || undefined, page: Math.max(1, Number(sp.page) || 1), pageSize: 20,
    from: sp.from ? fromLocalInput(`${sp.from}T00:00`) : undefined, to: sp.to ? fromLocalInput(`${sp.to}T23:59`) : undefined,
    publisherId: admin && sp.publisherId !== undefined && sp.publisherId !== "" ? Number(sp.publisherId) : undefined,
  };
  const [list, publishers] = await Promise.all([
    load(admin ? client.GET("/api/admin/auctions", { params: { query } }) : client.GET("/api/seller/auctions", { params: { query } })),
    admin ? load(client.GET("/api/admin/publishers/options")) : Promise.resolve([]),
  ]);
  const filtered = !!(status || sp.q || sp.from || sp.to || query.publisherId !== undefined);
  const input = "h-9 rounded-md border border-line-strong bg-surface px-2 text-sm";

  return (
    <>
      <PageHeader
        title={t("listTitle")}
        description={admin ? t("adminListDescription") : t("sellerListDescription")}
        actions={
          <Link href={`${routeBase}/new`} className="inline-flex h-10 items-center gap-2 rounded-md bg-patina px-4 text-sm font-medium text-white hover:bg-patina-700">
            <Plus className="h-4 w-4" aria-hidden /> {t("newTitle")}
          </Link>
        }
      />
      <nav className="mb-4 flex gap-1 border-b border-line" aria-label={t("listTitle")}>
        {(["active", "archived", "all"] as const).map((v) => (
          <Link key={v} href={`${routeBase}?view=${v}`} aria-current={view === v ? "page" : undefined} className={cn("-mb-px border-b-2 px-3 py-2.5 text-sm font-medium", view === v ? "border-patina text-ink" : "border-transparent text-muted hover:text-ink")}>
            {v === "active" ? t("active") : v === "archived" ? t("archived") : t("all")}
          </Link>
        ))}
      </nav>
      <form className="mb-4 flex flex-wrap items-end gap-2" aria-label={t("filter")}>
        <input type="hidden" name="view" value={view} />
        <label className="flex flex-col gap-1 text-xs text-muted">
          {t("search")}
          <input name="q" type="search" defaultValue={sp.q} className={input} />
        </label>
        <label className="flex flex-col gap-1 text-xs text-muted">
          {t("phase")}
          <select name="status" defaultValue={status ?? ""} className={input}>
            <option value="">{t("statusAny")}</option>
            <option value="live">{tl("statusLive")}</option>
            <option value="upcoming">{tl("statusUpcoming")}</option>
            <option value="ended">{tl("statusEnded")}</option>
          </select>
        </label>
        <label className="flex flex-col gap-1 text-xs text-muted">
          {t("from")}
          <input name="from" type="date" defaultValue={sp.from} className={input} />
        </label>
        <label className="flex flex-col gap-1 text-xs text-muted">
          {t("to")}
          <input name="to" type="date" defaultValue={sp.to} className={input} />
        </label>
        {admin && (
          <label className="flex flex-col gap-1 text-xs text-muted">
            {t("publisher")}
            <select name="publisherId" defaultValue={sp.publisherId ?? ""} className={input}>
              <option value="">{t("publisherAny")}</option>
              <option value="0">{t("platform")}</option>
              {publishers.map((p) => (
                <option key={p.id} value={p.id}>{p.titleEn}</option>
              ))}
            </select>
          </label>
        )}
        <button className="h-9 rounded-md bg-patina px-4 text-sm font-bold text-white hover:bg-patina-700">{t("filter")}</button>
        {filtered && (
          <Link href={`${routeBase}?view=${view}`} className="inline-flex h-9 items-center px-2 text-sm text-ink-2 hover:underline">
            {t("clear")}
          </Link>
        )}
      </form>

      {list.items.length === 0 ? (
        <EmptyState
          icon={<Gavel className="h-8 w-8" />}
          title={filtered || view !== "active" ? t("empty") : t("emptyFirst")}
          action={<Link href={`${routeBase}/new`} className="text-sm font-medium text-patina hover:underline">{t("newTitle")}</Link>}
        />
      ) : (
        <Table>
          <thead>
            <tr>
              <th className="w-16"><span className="sr-only">{t("title")}</span></th>
              <th>{t("title")}</th>
              {admin && <th>{t("publisher")}</th>}
              <th>{t("price")}</th>
              <th>{t("start")}</th>
              <th>{t("end")}</th>
              <th>{t("phase")}</th>
              <th>{t("bids")}</th>
              <th>{t("visible")}</th>
              <th><span className="sr-only">{t("actions")}</span></th>
            </tr>
          </thead>
          <tbody>
            {list.items.map((a) => {
              const title = pick(locale, a.titleEn, a.titleAr);
              return (
                <tr key={a.id}>
                  <td>
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    {a.mainImageFileId ? <img src={fileUrl(a.mainImageFileId)} alt="" className="h-10 w-14 rounded object-cover" /> : null}
                  </td>
                  <td>
                    <Link href={`${routeBase}/${a.id}`} className="font-medium hover:underline">{title}</Link>
                    <p className="tabular text-xs text-muted" dir="ltr">{a.refNumber}</p>
                  </td>
                  {admin && <td className="text-ink-2">{a.publisherName ?? <Badge tone="brand">{t("platform")}</Badge>}</td>}
                  <td className="tabular">{money(a.currentPrice, locale)}</td>
                  <td className="whitespace-nowrap text-muted">{date(a.startsAtUtc, locale)}</td>
                  <td className="whitespace-nowrap text-muted">{date(a.endsAtUtc, locale)}</td>
                  <td><PhaseBadge phase={a.phase} /></td>
                  <td className="tabular">{a.bidCount}</td>
                  <td><ActiveSwitch base={base} id={a.id} active={a.isActive} label={title} /></td>
                  <td><RowActions base={base} id={a.id} routeBase={routeBase} title={title} /></td>
                </tr>
              );
            })}
          </tbody>
        </Table>
      )}
      <Pagination page={list.page} totalPages={Math.ceil(list.total / list.pageSize)} pathname={routeBase} query={sp} />
    </>
  );
}

export async function BidsTable({ admin, auctionId, page }: { admin: boolean; auctionId: number; page: number }) {
  const [t, locale, client] = await Promise.all([getTranslations("seller.manage.bidsTab"), getLocale(), serverApi()]);
  const params = { params: { path: { id: auctionId }, query: { page } } };
  const r = await load(admin ? client.GET("/api/admin/auctions/{id}/bids", params) : client.GET("/api/seller/auctions/{id}/bids", params));
  if (r.items.length === 0) return <EmptyState title={t("empty")} />;
  return (
    <Table>
      <thead>
        <tr><th>{t("bidder")}</th><th>{t("contact")}</th><th>{t("amount")}</th><th>{t("time")}</th></tr>
      </thead>
      <tbody>
        {r.items.map((b) => (
          <tr key={b.id}>
            <td className="font-medium">
              {b.userName} {b.isLeading && <Badge tone="live" className="ms-1">{t("leading")}</Badge>}
            </td>
            <td className="text-sm text-ink-2">
              <a href={`mailto:${b.email}`} className="block hover:underline">{b.email}</a>
              {b.phone && <a href={`tel:${b.phone}`} className="block hover:underline" dir="ltr">{b.phone}</a>}
            </td>
            <td className="tabular font-semibold">{money(b.amount, locale)}</td>
            <td className="whitespace-nowrap text-muted">{date(b.createdAtUtc, locale)}</td>
          </tr>
        ))}
      </tbody>
    </Table>
  );
}
