import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getLocale, getTranslations } from "next-intl/server";
import { PageHeader, Panel } from "@/components/ui/display";
import { Table } from "@/components/ui/controls";
import { Link } from "@/i18n/navigation";
import { load, serverApi } from "@/lib/api/server";
import { dateTime, money, pick } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("seller.bidders"))("title") };
}

export default async function SellerBidder({ params }: { params: Promise<{ userId: string }> }) {
  const userId = Number((await params).userId);
  if (!Number.isInteger(userId) || userId <= 0) notFound();
  const [t, locale, client] = await Promise.all([getTranslations("seller.bidders"), getLocale(), serverApi()]);
  const d = await load(client.GET("/api/seller/bidders/{userId}", { params: { path: { userId } } }));
  return (
    <>
      <Link href="/seller/bidders" className="text-sm text-patina hover:underline">{t("back")}</Link>
      <PageHeader title={d.bidder.fullName} description={t("privacy")} />
      <Panel>
        <dl className="grid gap-3 text-sm sm:grid-cols-3">
          <div><dt className="text-muted">{t("email")}</dt><dd><a href={`mailto:${d.bidder.email}`} className="hover:underline">{d.bidder.email}</a></dd></div>
          <div><dt className="text-muted">{t("phone")}</dt><dd dir="ltr" className="text-start">{d.bidder.phone ?? "—"}</dd></div>
          <div><dt className="text-muted">{t("company")}</dt><dd>{d.companyName ?? "—"}</dd></div>
        </dl>
      </Panel>
      <h2 className="mb-3 mt-6 text-lg font-semibold">{t("theirBids")}</h2>
      <Table>
        <tbody>
          {d.bids.map((b) => (
            <tr key={b.id}>
              <td><Link href={`/seller/auctions/${b.auctionId}`} className="font-medium hover:underline">{pick(locale, b.titleEn, b.titleAr)}</Link></td>
              <td className="tabular font-semibold">{money(b.amount, locale)}</td>
              <td className="whitespace-nowrap text-muted">{dateTime(b.createdAtUtc, locale)}</td>
            </tr>
          ))}
        </tbody>
      </Table>
    </>
  );
}
