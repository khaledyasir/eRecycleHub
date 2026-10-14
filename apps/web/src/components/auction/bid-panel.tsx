"use client";

import { useQueryClient } from "@tanstack/react-query";
import { Lock, Minus, Plus, ShieldCheck, Trophy } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useId, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Dialog } from "@/components/ui/overlay";
import { useSession } from "@/components/providers";
import { Link } from "@/i18n/navigation";
import { ApiError, api, call } from "@/lib/api/client";
import type { AuctionPhase } from "@/lib/api/types";
import { cn, money } from "@/lib/format";
import { useErrorMessage } from "@/lib/use-error-message";
import { Countdown } from "./countdown";
import { useAuctionBids } from "./live";

type Props = {
  auctionId: number;
  refNumber: string;
  phase: AuctionPhase;
  startsAtUtc: string;
  endsAtUtc: string | null;
  currentPrice: number;
  minimumNextBid: number;
  increment: number;
  insurance: number;
  bidCount: number;
  serverNow: number;
  viewer: { isOwner: boolean; walletBalance: number | null; isLeading: boolean };
};

/** Two-decimal arithmetic without floating-point drift (amounts are cents-precise in the API). */
const cents = (n: number) => Math.round(n * 100);
const fromCents = (c: number) => c / 100;

/**
 * The bid box. Every rule shown here is enforced again by the API inside a locked transaction; this component only
 * helps the bidder enter a valid amount and explains why bidding may be unavailable.
 */
export function BidPanel(p: Props) {
  const t = useTranslations("auction.bid");
  const td = useTranslations("auction.detail");
  const tc = useTranslations("common");
  const locale = useLocale();
  const user = useSession();
  const message = useErrorMessage();
  const qc = useQueryClient();
  const inputId = useId();

  const [phase, setPhase] = useState<AuctionPhase>(p.phase);
  const [price, setPrice] = useState(p.currentPrice);
  const [minimum, setMinimum] = useState(p.minimumNextBid);
  const [count, setCount] = useState(p.bidCount);
  const [leading, setLeading] = useState(p.viewer.isLeading);
  const [amount, setAmount] = useState(String(p.minimumNextBid));
  const [accepted, setAccepted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);

  // Notice bids from other people while the page is open.
  const latest = useAuctionBids(p.auctionId, 1, phase === "Live");
  useEffect(() => {
    // Syncing polled server data into local state the bidder can also edit (the amount field), so it can't be purely derived.
    /* eslint-disable react-hooks/set-state-in-effect */
    const top = latest.data?.items[0];
    if (top && cents(top.amount) > cents(price)) {
      setPrice(top.amount);
      setMinimum(fromCents(cents(top.amount) + cents(p.increment)));
      setCount(latest.data!.total);
      setLeading(top.isMine);
      setAmount((a) => (cents(Number(a)) < cents(top.amount) + cents(p.increment) ? String(fromCents(cents(top.amount) + cents(p.increment))) : a));
    }
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [latest.data, price, p.increment]);

  const toLive = useCallback(() => setPhase("Live"), []);
  const toEnded = useCallback(() => setPhase("Ended"), []);

  const value = Number(amount);
  const validNumber = amount.trim() !== "" && Number.isFinite(value) && /^\d+(\.\d{1,2})?$/.test(amount.trim());
  const step = (dir: 1 | -1) => {
    const base = validNumber ? cents(value) : cents(minimum);
    const next = Math.max(cents(minimum), base + dir * cents(p.increment));
    setAmount(String(fromCents(next)));
    setError(null);
  };

  const wallet = p.viewer.walletBalance ?? 0;
  const lacksInsurance = !!user && wallet < p.insurance;
  const blocked = !user ? "signIn" : p.viewer.isOwner ? "own" : user.role === "Admin" ? "admin" : phase === "Upcoming" ? "upcoming" : phase === "Ended" ? "ended" : lacksInsurance ? "insurance" : null;

  const review = () => {
    if (!validNumber) return setError(t("invalid"));
    if (cents(value) < cents(minimum)) return setError(t("belowMin", { minimum: money(minimum, locale) }));
    if (!accepted) return setError(t("termsRequired"));
    setError(null);
    setConfirming(true);
  };

  const place = async () => {
    setBusy(true);
    try {
      const r = await call(() => api.POST("/api/auctions/{id}/bids", { params: { path: { id: p.auctionId } }, body: { amount: value } }));
      setPrice(r.currentPrice);
      setMinimum(r.minimumNextBid);
      setCount(r.bidCount);
      setLeading(true);
      setAmount(String(r.minimumNextBid));
      toast.success(t("success", { amount: money(r.currentPrice, locale) }));
      void qc.invalidateQueries({ queryKey: ["auction-bids", p.auctionId] });
    } catch (e) {
      if (e instanceof ApiError && e.code === "bid.below_minimum" && typeof e.args?.minimum === "number") {
        const min = e.args.minimum;
        setMinimum(min);
        setPrice(fromCents(cents(min) - cents(p.increment)));
        setAmount(String(min));
      }
      if (e instanceof ApiError && e.code === "bid.ended") setPhase("Ended");
      setError(message(e));
      toast.error(message(e));
    } finally {
      setBusy(false);
      setConfirming(false);
    }
  };

  return (
    <section aria-labelledby="bid-title" className="overflow-hidden rounded-lg border border-line bg-surface">
      <div className={cn("px-5 py-4", phase === "Live" ? "bg-hivis-soft" : "bg-ground")}>
        <p className="text-sm text-ink-2">{count > 0 ? td("currentPrice") : td("startPrice")}</p>
        <p className="tabular text-3xl font-semibold" aria-live="polite">
          {money(price, locale)}
        </p>
        <p className="tabular mt-1 text-sm text-ink-2">{td("bidders", { count })}</p>
        <div className="mt-4">
          {phase === "Upcoming" && (
            <>
              <p className="mb-1.5 text-sm font-medium">{t("notStarted")}</p>
              <Countdown target={p.startsAtUtc} serverNow={p.serverNow} mode="start" size="lg" onDone={toLive} />
            </>
          )}
          {phase === "Live" && <Countdown target={p.endsAtUtc} serverNow={p.serverNow} size="lg" onDone={toEnded} />}
          {phase === "Ended" && <p className="font-semibold text-ink-2">{t("ended")}</p>}
        </div>
      </div>

      <div className="space-y-4 px-5 py-5">
        {leading && phase !== "Upcoming" && (
          <Alert tone="ok">
            <span className="inline-flex items-center gap-2 font-medium">
              <Trophy className="h-4 w-4" aria-hidden /> {t("leading")}
            </span>
          </Alert>
        )}

        <dl className="grid grid-cols-2 gap-3 text-sm">
          <div>
            <dt className="text-muted">{t("minimum")}</dt>
            <dd className="tabular font-semibold">{money(minimum, locale)}</dd>
          </div>
          <div>
            <dt className="text-muted">{td("increment")}</dt>
            <dd className="tabular font-semibold">{money(p.increment, locale)}</dd>
          </div>
          <div className="col-span-2">
            <dt className="flex items-center gap-1 text-muted">
              <ShieldCheck className="h-4 w-4" aria-hidden /> {td("insurance")}
            </dt>
            <dd className="tabular font-semibold">{p.insurance > 0 ? money(p.insurance, locale) : td("noInsurance")}</dd>
            {p.insurance > 0 && <p className="mt-0.5 text-xs text-muted">{td("insuranceHelp")}</p>}
          </div>
          {user && p.viewer.walletBalance !== null && (
            <div className="col-span-2 flex items-center justify-between rounded-md bg-ground px-3 py-2">
              <span className="text-muted">{t("wallet")}</span>
              <span className="tabular font-semibold">{money(wallet, locale)}</span>
            </div>
          )}
        </dl>

        {blocked === "signIn" && (
          <div className="space-y-3">
            <p className="text-sm text-ink-2">{t("signInBody")}</p>
            <Button asChild variant="bid" size="lg" className="w-full">
              <Link href={`/login?next=${encodeURIComponent(`/auctions/${p.auctionId}`)}`}>
                <Lock className="h-4 w-4" aria-hidden /> {t("signIn")}
              </Link>
            </Button>
          </div>
        )}
        {blocked === "own" && <Alert tone="info">{t("ownAuction")}</Alert>}
        {blocked === "admin" && <Alert tone="info">{t("admin")}</Alert>}
        {blocked === "insurance" && phase === "Live" && (
          <Alert tone="warn">
            <p>{t("insufficient", { insurance: money(p.insurance, locale) })}</p>
            <Link href="/account/wallet" className="mt-2 inline-block font-semibold underline">
              {t("topUp")}
            </Link>
          </Alert>
        )}

        {blocked === null && (
          <form
            noValidate
            onSubmit={(e) => {
              e.preventDefault();
              review();
            }}
            className="space-y-3"
          >
            <label htmlFor={inputId} className="block text-sm font-medium">
              {t("yourBid")}
            </label>
            <div className="flex items-stretch gap-2">
              <Button type="button" variant="secondary" size="icon" onClick={() => step(-1)} aria-label={t("decrease")} disabled={busy}>
                <Minus className="h-4 w-4" />
              </Button>
              <input
                id={inputId}
                inputMode="decimal"
                autoComplete="off"
                value={amount}
                onChange={(e) => {
                  setAmount(e.target.value.replace(",", "."));
                  setError(null);
                }}
                aria-invalid={!!error}
                aria-describedby={error ? `${inputId}-err` : undefined}
                className="tabular h-10 min-w-0 flex-1 rounded-md border border-line-strong bg-surface px-3 text-center text-lg font-semibold focus:border-patina-500 focus:outline-none focus:ring-2 focus:ring-patina-100 aria-[invalid=true]:border-rust"
                dir="ltr"
              />
              <Button type="button" variant="secondary" size="icon" onClick={() => step(1)} aria-label={t("increase")} disabled={busy}>
                <Plus className="h-4 w-4" />
              </Button>
            </div>
            <label className="flex cursor-pointer items-start gap-2 text-sm">
              <input type="checkbox" checked={accepted} onChange={(e) => setAccepted(e.target.checked)} className="mt-1 accent-patina" />
              <span>{t("terms")}</span>
            </label>
            {error && (
              <p id={`${inputId}-err`} role="alert" className="text-sm font-medium text-rust">
                {error}
              </p>
            )}
            <Button type="submit" variant="bid" size="lg" className="w-full" loading={busy}>
              {t("place")}
            </Button>
          </form>
        )}

        <details className="text-sm">
          <summary className="cursor-pointer font-medium text-ink-2">{t("whatNext")}</summary>
          <p className="mt-2 text-muted">{t("whatNextBody")}</p>
        </details>
      </div>

      <Dialog
        open={confirming}
        onOpenChange={(o) => !busy && setConfirming(o)}
        title={t("confirmTitle")}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirming(false)} disabled={busy}>
              {tc("cancel")}
            </Button>
            <Button variant="bid" onClick={place} loading={busy}>
              {t("confirm", { amount: validNumber ? money(value, locale) : amount })}
            </Button>
          </>
        }
      >
        <p className="text-sm text-ink-2">{t("confirmBody", { amount: validNumber ? money(value, locale) : amount, ref: p.refNumber })}</p>
      </Dialog>
    </section>
  );
}
