"use client";

import { HttpTransportType, HubConnectionBuilder } from "@microsoft/signalr";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { api, call } from "@/lib/api/client";

/**
 * Latest page of public bids for an auction. Shared by the bid panel (to notice the price moving) and the history table.
 * While the auction is live, the API pushes `bid.placed` over SignalR (/hubs/auctions) and we refetch right away; the 10-second
 * poll only runs while the socket is down. Refetching (instead of trusting the push payload) keeps `isMine` and the list exact.
 */
export function useAuctionBids(auctionId: number, page: number, live: boolean) {
  const qc = useQueryClient();
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    if (!live) return;
    const refresh = () => void qc.invalidateQueries({ queryKey: ["auction-bids", auctionId] });
    const conn = new HubConnectionBuilder()
      .withUrl("/hubs/auctions", { transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling })
      .withAutomaticReconnect()
      .build();
    const join = () => conn.invoke("Watch", auctionId).then(() => setConnected(true), () => setConnected(false));
    conn.on("bid.placed", (e: { auctionId: number }) => e.auctionId === auctionId && refresh());
    conn.onreconnecting(() => setConnected(false));
    conn.onreconnected(() => void join().then(refresh)); // missed events are gone: catch up
    conn.onclose(() => setConnected(false));
    conn.start().then(join, () => setConnected(false)); // ponytail: no retry after the first failed start; the poll covers it until the page reloads
    return () => {
      setConnected(false);
      void conn.stop();
    };
  }, [auctionId, live, qc]);

  return useQuery({
    queryKey: ["auction-bids", auctionId, page],
    queryFn: () => call(() => api.GET("/api/auctions/{id}/bids", { params: { path: { id: auctionId }, query: { page } } })),
    refetchInterval: live && !connected ? 10_000 : false,
  });
}
