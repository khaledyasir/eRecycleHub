import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { AuctionEditor } from "@/components/manage/auction-editor";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("seller.manage"))("editTitle") };
}

export default async function EditSellerAuction({ params }: { params: Promise<{ id: string }> }) {
  const id = Number((await params).id);
  if (!Number.isInteger(id) || id <= 0) notFound();
  return <AuctionEditor admin={false} id={id} />;
}
