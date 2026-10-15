import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuctionEditor } from "@/components/manage/auction-editor";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("seller.manage"))("newTitle") };
}

export default function NewAdminAuction() {
  return <AuctionEditor admin />;
}
