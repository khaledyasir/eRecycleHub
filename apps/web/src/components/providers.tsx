"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Direction } from "radix-ui";
import { createContext, useContext, useState, type ReactNode } from "react";
import { Toaster } from "sonner";
import type { SessionUser } from "@/lib/api/types";

const SessionContext = createContext<SessionUser | null>(null);

/** The signed-in user as resolved on the server for this request (null when anonymous). */
export const useSession = () => useContext(SessionContext);

export function Providers({ user, dir, children }: { user: SessionUser | null; dir: "ltr" | "rtl"; children: ReactNode }) {
  const [client] = useState(() => new QueryClient({ defaultOptions: { queries: { staleTime: 15_000, retry: 1, refetchOnWindowFocus: false } } }));
  return (
    <SessionContext.Provider value={user}>
      <QueryClientProvider client={client}>
        {/* Radix components assume LTR unless told otherwise (tabs, menus, selects, keyboard arrows). */}
        <Direction.Provider dir={dir}>{children}</Direction.Provider>
        <Toaster position={dir === "rtl" ? "bottom-left" : "bottom-right"} dir={dir} richColors closeButton toastOptions={{ duration: 5000 }} />
      </QueryClientProvider>
    </SessionContext.Provider>
  );
}
