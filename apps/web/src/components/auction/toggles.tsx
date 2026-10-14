"use client";

import { Bell, BellRing, Heart } from "lucide-react";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { useSession } from "@/components/providers";
import { Link, usePathname } from "@/i18n/navigation";
import { api, call } from "@/lib/api/client";
import { cn } from "@/lib/format";
import { useErrorMessage } from "@/lib/use-error-message";

function useToggle(initial: boolean, put: () => Promise<unknown>, del: () => Promise<unknown>) {
  const [on, setOn] = useState(initial);
  const [busy, setBusy] = useState(false);
  const message = useErrorMessage();
  const toggle = async () => {
    const next = !on;
    setOn(next); // optimistic; reverted on failure
    setBusy(true);
    try {
      await (next ? put() : del());
      return next;
    } catch (e) {
      setOn(!next);
      toast.error(message(e));
      return null;
    } finally {
      setBusy(false);
    }
  };
  return { on, busy, toggle };
}

function SignInLink({ className, children, label }: { className: string; children: React.ReactNode; label: string }) {
  const pathname = usePathname();
  return (
    <Link href={`/login?next=${encodeURIComponent(pathname)}`} className={className} aria-label={label}>
      {children}
    </Link>
  );
}

export function FavoriteButton({ auctionId, initial, variant = "icon" }: { auctionId: number; initial: boolean; variant?: "icon" | "full" }) {
  const t = useTranslations("auction.actions");
  const user = useSession();
  const path = { params: { path: { id: auctionId } } };
  const { on, busy, toggle } = useToggle(
    initial,
    () => call(() => api.PUT("/api/auctions/{id}/favorite", path)),
    () => call(() => api.DELETE("/api/auctions/{id}/favorite", path)),
  );
  const cls =
    variant === "icon"
      ? "inline-flex h-9 w-9 items-center justify-center rounded-full bg-surface/95 text-ink shadow-raise hover:text-rust"
      : "inline-flex h-10 items-center gap-2 rounded-md border border-line-strong bg-surface px-3 text-sm font-medium hover:bg-sunken";
  const label = on ? t("unfavorite") : t("favorite");
  const icon = <Heart className={cn("h-4 w-4", on && "fill-rust text-rust")} aria-hidden />;
  if (!user) return <SignInLink className={cls} label={t("favorite")}>{icon}{variant === "full" && t("favorite")}</SignInLink>;
  return (
    <button type="button" onClick={toggle} disabled={busy} aria-pressed={on} aria-label={label} className={cls}>
      {icon}
      {variant === "full" && label}
    </button>
  );
}

export function WatchButton({ auctionId, initial }: { auctionId: number; initial: boolean }) {
  const t = useTranslations("auction.actions");
  const user = useSession();
  const path = { params: { path: { id: auctionId } } };
  const { on, busy, toggle } = useToggle(
    initial,
    () => call(() => api.PUT("/api/auctions/{id}/watch", path)),
    () => call(() => api.DELETE("/api/auctions/{id}/watch", path)),
  );
  const cls = "inline-flex h-10 items-center gap-2 rounded-md border border-line-strong bg-surface px-3 text-sm font-medium hover:bg-sunken aria-pressed:border-patina aria-pressed:bg-patina-50 aria-pressed:text-patina-700";
  if (!user) return <SignInLink className={cls} label={t("watch")}><Bell className="h-4 w-4" aria-hidden />{t("watch")}</SignInLink>;
  return (
    <button
      type="button"
      disabled={busy}
      aria-pressed={on}
      className={cls}
      onClick={async () => {
        const result = await toggle();
        if (result !== null) toast.success(result ? t("watchOn") : t("watchOff"));
      }}
    >
      {on ? <BellRing className="h-4 w-4" aria-hidden /> : <Bell className="h-4 w-4" aria-hidden />}
      {on ? t("watching") : t("watch")}
    </button>
  );
}
