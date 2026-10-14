"use client";

import { useQuery } from "@tanstack/react-query";
import { api, call } from "@/lib/api/client";

/**
 * Latest page of public bids for an auction. Shared by the bid panel (to notice the price moving) and the history table.
 * Polls every 10 seconds while the auction is live.
 */
export function useAuctionBids(auctionId: number, page: number, live: boolean) {
  return useQuery({
    queryKey: ["auction-bids", auctionId, page],
    queryFn: () => call(() => api.GET("/api/auctions/{id}/bids", { params: { path: { id: auctionId }, query: { page } } })),
    refetchInterval: live ? 10_000 : false,
  });
}
