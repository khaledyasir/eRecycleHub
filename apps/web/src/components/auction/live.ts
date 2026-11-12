"use client";

import { HttpTransportType, HubConnectionBuilder } from "@microsoft/signalr";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { api, call } from "@/lib/api/client";

/**
 * Latest page of public bids for an auction. Shared by the bid panel (to notice the price moving) and the history table.
 * While the auction is live, the API pushes `bid.placed` over SignalR (/hubs/auctions) and we refetch right away; the 10-second
 * poll only runs while the socket is down. Refetching (instead of trusting the push payload) keeps `isMine` and the list exact.
 * The same socket carries `auction.started`, `auction.extended { endDate }` and `auction.ended` for the watched auction (`events`).
 */
export type AuctionLiveEvents = {
  /** Upcoming → live. */
  onStarted?: () => void;
  /** The end moved while the auction runs (a late bid extended it). */
  onExtended?: (endsAtUtc: string) => void;
  onEnded?: () => void;
  /** The socket came back: lifecycle events sent meanwhile are gone, so ask the server. */
  onResync?: () => void;
};

export function useAuctionBids(auctionId: number, page: number, live: boolean, events?: AuctionLiveEvents) {
  const qc = useQueryClient();
  const [connected, setConnected] = useState(false);
  // The latest handlers, without reconnecting when the caller passes a new object.
  const handlers = useRef(events);
  useEffect(() => {
    handlers.current = events;
  });

  useEffect(() => {
    if (!live) return;
    const refresh = () => {
      void qc.invalidateQueries({ queryKey: ["auction-bids", auctionId] });
    };
    const conn = new HubConnectionBuilder()
      .withUrl("/hubs/auctions", { transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling })
      .withAutomaticReconnect()
      .build();
    const join = () => conn.invoke("Watch", auctionId).then(() => setConnected(true), () => setConnected(false));
    conn.on("bid.placed", (e: { auctionId: number }) => e.auctionId === auctionId && refresh());
    // Lifecycle events reach only the connections watching this auction (the hub group), so they carry no auction id.
    conn.on("auction.started", () => handlers.current?.onStarted?.());
    conn.on("auction.extended", (e: { endDate?: string }) => e?.endDate && handlers.current?.onExtended?.(e.endDate));
    conn.on("auction.ended", () => handlers.current?.onEnded?.());
    conn.onreconnecting(() => setConnected(false));
    conn.onreconnected(
      () =>
        void join().then(() => {
          refresh(); // missed events are gone: catch up
          handlers.current?.onResync?.();
        }),
    );
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
