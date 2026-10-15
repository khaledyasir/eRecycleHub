import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuctionList } from "@/components/manage/auction-list";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("seller.manage"))("listTitle") };
}

export default async function AdminAuctions({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  return <AuctionList admin sp={await searchParams} />;
}
