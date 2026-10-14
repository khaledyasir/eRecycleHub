import type { Metadata } from "next";
import { Eye } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { StopWatchButton } from "@/components/account/account-forms";
import { AuctionCard } from "@/components/auction/auction-card";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";

import { nowMs } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.lists"))("watchingTitle") };
}

export default async function Watching({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, client] = await Promise.all([getTranslations("account.lists"), serverApi()]);
  const r = await load(client.GET("/api/account/watching", { params: { query: { page: Math.max(1, Number(sp.page) || 1) } } }));
  const now = nowMs();
  return (
    <>
      <PageHeader title={t("watchingTitle")} description={t("watchingDescription")} />
      {r.items.length === 0 ? (
        <EmptyState icon={<Eye className="h-8 w-8" />} title={t("watchingEmpty")} action={<Link href="/auctions" className="text-sm font-medium text-patina hover:underline">{t("browse")}</Link>} />
      ) : (
        <ul className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {r.items.map((a) => (
            <li key={a.id} className="flex flex-col gap-2">
              <AuctionCard a={a} serverNow={now} className="flex-1" />
              <StopWatchButton auctionId={a.id} />
            </li>
          ))}
        </ul>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/account/watching" query={sp} />
    </>
  );
}
