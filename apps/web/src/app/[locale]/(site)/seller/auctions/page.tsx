import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuctionList } from "@/components/manage/auction-list";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("seller.manage"))("listTitle") };
}

export default async function SellerAuctions({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  return <AuctionList admin={false} sp={await searchParams} />;
}
