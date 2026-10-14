import { ExternalLink } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { PhaseBadge } from "@/components/auction/auction-card";
import { PageHeader } from "@/components/ui/display";
import { Tabs } from "@/components/ui/controls";
import { Link } from "@/i18n/navigation";
import { getCountryId, load, serverApi } from "@/lib/api/server";
import { pick } from "@/lib/format";
import { AuctionForm } from "./auction-form";
import { BidsTable } from "./auction-list";
import { ImageManager, NotifyWinnerButton } from "./manage-actions";

/** New/edit page body for an auction, used by /seller/auctions/* and /admin/auctions/*. A foreign id is a 404 from the API. */
export async function AuctionEditor({ admin, id }: { admin: boolean; id?: number }) {
  const [t, locale, client, countryId] = await Promise.all([getTranslations("seller.manage"), getLocale(), serverApi(), getCountryId()]);
  const base = admin ? ("/api/admin" as const) : ("/api/seller" as const);
  const routeBase = admin ? ("/admin/auctions" as const) : ("/seller/auctions" as const);
  const [countries, publishers, auction] = await Promise.all([
    load(client.GET("/api/countries")),
    admin ? load(client.GET("/api/admin/publishers/options")) : Promise.resolve(undefined),
    id === undefined ? Promise.resolve(undefined) : load(admin ? client.GET("/api/admin/auctions/{id}", { params: { path: { id } } }) : client.GET("/api/seller/auctions/{id}", { params: { path: { id } } })),
  ]);
  const form = (
    <div className="rounded-lg border border-line bg-surface p-5 sm:p-7">
      <AuctionForm base={base} admin={admin} countries={countries} publishers={publishers} auction={auction} defaultCountryId={countryId} routeBase={routeBase} />
    </div>
  );
  if (!auction) return (<><PageHeader title={t("newTitle")} />{form}</>);

  return (
    <>
      <PageHeader
        title={pick(locale, auction.titleEn, auction.titleAr)}
        description={`${t("lot")} ${auction.refNumber}`}
        actions={
          <>
            <PhaseBadge phase={auction.phase} />
            {admin && auction.phase === "Ended" && auction.bidCount > 0 && <NotifyWinnerButton auctionId={auction.id} />}
            <Link href={`/auctions/${auction.id}`} target="_blank" className="inline-flex h-10 items-center gap-2 rounded-md border border-line-strong bg-surface px-3 text-sm font-medium hover:bg-sunken">
              <ExternalLink className="h-4 w-4" aria-hidden /> {t("view")}
            </Link>
          </>
        }
      />
      <Tabs
        items={[
          { value: "details", label: t("tabs.details"), content: form },
          { value: "photos", label: `${t("tabs.photos")} (${auction.images.length})`, content: <ImageManager base={base} auctionId={auction.id} images={auction.images} /> },
          { value: "bids", label: `${t("tabs.bids")} (${auction.bidCount})`, content: <BidsTable admin={admin} auctionId={auction.id} page={1} /> },
        ]}
      />
    </>
  );
}
