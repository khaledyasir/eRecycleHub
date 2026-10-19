import type { Metadata } from "next";
import { Star } from "lucide-react";
import { getLocale, getTranslations } from "next-intl/server";
import { DeleteReviewButton } from "@/components/seller/seller-forms";
import { EmptyState, PageHeader } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Pagination } from "@/components/ui/pagination";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { date, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("seller.reviews"))("title") };
}

export default async function SellerReviews({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, locale, client] = await Promise.all([getTranslations("seller.reviews"), getLocale(), serverApi()]);
  const r = await load(client.GET("/api/seller/reviews", { params: { query: { page: Math.max(1, Number(sp.page) || 1) } } }));
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      {r.items.length === 0 ? (
        <EmptyState icon={<Star className="h-8 w-8" />} title={t("empty")} />
      ) : (
        <Table>
          <thead><tr><th>{t("auction")}</th><th>{t("rating")}</th><th>{t("comment")}</th><th>{t("by")}</th><th>{t("date")}</th><th><span className="sr-only">{t("delete")}</span></th></tr></thead>
          <tbody>
            {r.items.map((x) => (
              <tr key={x.id}>
                <td><Link href={`/auctions/${x.auctionId}`} className="font-medium hover:underline">{pick(locale, x.auctionTitleEn, x.auctionTitleAr)}</Link></td>
                <td className="tabular whitespace-nowrap" aria-label={`${x.rating}/5`}>{"★".repeat(x.rating)}<span className="text-line-strong">{"★".repeat(5 - x.rating)}</span></td>
                <td className="max-w-md text-ink-2">{x.comment ?? "—"}</td>
                <td><bdi>{x.name}</bdi></td>
                <td className="whitespace-nowrap text-muted">{date(x.createdAtUtc, locale)}</td>
                <td><DeleteReviewButton id={x.id} /></td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/seller/reviews" query={sp} />
    </>
  );
}
