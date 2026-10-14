import type { Metadata } from "next";
import { Heart } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { AuctionGrid } from "@/components/auction/auction-card";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";

import { nowMs } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.lists"))("favoritesTitle") };
}

export default async function Favorites({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, client] = await Promise.all([getTranslations("account.lists"), serverApi()]);
  const r = await load(client.GET("/api/account/favorites", { params: { query: { page: Math.max(1, Number(sp.page) || 1) } } }));
  return (
    <>
      <PageHeader title={t("favoritesTitle")} />
      {r.items.length === 0 ? (
        <EmptyState icon={<Heart className="h-8 w-8" />} title={t("favoritesEmpty")} action={<Link href="/auctions" className="text-sm font-medium text-patina hover:underline">{t("browse")}</Link>} />
      ) : (
        <AuctionGrid items={r.items} serverNow={nowMs()} className="xl:grid-cols-3" />
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/account/favorites" query={sp} />
    </>
  );
}
