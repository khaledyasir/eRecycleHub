import type { Metadata } from "next";
import { Trophy } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { AuctionGrid } from "@/components/auction/auction-card";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { cn, nowMs } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("account.auctions"))("title") };
}

export default async function MyAuctions({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const tab = sp.tab === "won" ? "won" : "leading";
  const [t, client] = await Promise.all([getTranslations("account.auctions"), serverApi()]);
  const r = await load(client.GET("/api/account/auctions", { params: { query: { tab, page: Math.max(1, Number(sp.page) || 1) } } }));
  return (
    <>
      <PageHeader title={t("title")} />
      <nav className="mb-5 flex gap-1 border-b border-line" aria-label={t("title")}>
        {(["leading", "won"] as const).map((x) => (
          <Link key={x} href={`/account/auctions?tab=${x}`} aria-current={tab === x ? "page" : undefined} className={cn("-mb-px border-b-2 px-3 py-2.5 text-sm font-medium", tab === x ? "border-patina text-ink" : "border-transparent text-muted hover:text-ink")}>
            {t(x)}
          </Link>
        ))}
      </nav>
      {r.items.length === 0 ? (
        <EmptyState icon={<Trophy className="h-8 w-8" />} title={tab === "won" ? t("wonEmpty") : t("leadingEmpty")} action={<Link href="/auctions" className="text-sm font-medium text-patina hover:underline">{t("browse")}</Link>} />
      ) : (
        <AuctionGrid items={r.items} serverNow={nowMs()} className="xl:grid-cols-3" />
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/account/auctions" query={sp} />
    </>
  );
}
