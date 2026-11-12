"use client";

import { useQueryClient } from "@tanstack/react-query";
import { Ban, Lock, Minus, Plus, ShieldCheck, Trophy } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useId, useRef, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Dialog } from "@/components/ui/overlay";
import { useSession } from "@/components/providers";
import { Link, useRouter } from "@/i18n/navigation";
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
  viewer: {
    isOwner: boolean;
    walletBalance: number | null;
    isLeading: boolean;
    heldByViewer: number;
    debitsWallet: boolean;
    /** After the end: the viewer holds the winning bid (and the sale was not cancelled). */
    isWinner: boolean;
    /** After the end: Paid | Pending | PaymentFailed | Voided | NotCharged, as the server states it. */
    saleStatus: string | null;
  };
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
  const router = useRouter();
  const inputId = useId();

  const [phase, setPhase] = useState<AuctionPhase>(p.phase);
  const [endsAt, setEndsAt] = useState(p.endsAtUtc);
  // router.refresh() brings the server's view of the auction (phase, end date): it replaces what this page worked out from live events.
  const fromServer = `${p.phase}|${p.endsAtUtc}`;
  const [seenServer, setSeenServer] = useState(fromServer);
  if (seenServer !== fromServer) {
    setSeenServer(fromServer);
    setPhase(p.phase);
    setEndsAt(p.endsAtUtc);
  }
  const [price, setPrice] = useState(p.currentPrice);
  const [minimum, setMinimum] = useState(p.minimumNextBid);
  const [count, setCount] = useState(p.bidCount);
  const [leading, setLeading] = useState(p.viewer.isLeading);
  // Wallet money moves while the page is open (a bid takes it, being outbid returns it): keep the figures shown in step.
  const [walletOverride, setWalletOverride] = useState<number | null>(null);
  const [heldNow, setHeldNow] = useState(p.viewer.heldByViewer);
  const [amount, setAmount] = useState(String(p.minimumNextBid));
  const [accepted, setAccepted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [slow, setSlow] = useState(false);

  // Notice bids from other people, and the auction starting, being extended or ending, while the page is open.
  const latest = useAuctionBids(p.auctionId, 1, phase !== "Ended", {
    onStarted: () => setPhase("Live"),
    onExtended: setEndsAt,
    onEnded: () => {
      setPhase("Ended");
      router.refresh(); // the outcome (winner, sale status) comes with the details
    },
    onResync: () => router.refresh(),
  });
  useEffect(() => {
    // Syncing polled server data into local state the bidder can also edit (the amount field), so it can't be purely derived.
    /* eslint-disable react-hooks/set-state-in-effect */
    const top = latest.data?.items[0];
    if (top && cents(top.amount) > cents(price)) {
      setPrice(top.amount);
      setMinimum(fromCents(cents(top.amount) + cents(p.increment)));
      setCount(latest.data!.total);
      if (leading && !top.isMine && p.viewer.debitsWallet) {
        // Outbid: the hold on that bid was released, so the money is available again (nothing was ever taken from the balance).
        setWalletOverride((w) => (w ?? p.viewer.walletBalance ?? 0) + heldNow);
        setHeldNow(0);
      }
      setLeading(top.isMine);
      setAmount((a) => (cents(Number(a)) < cents(top.amount) + cents(p.increment) ? String(fromCents(cents(top.amount) + cents(p.increment))) : a));
    }
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [latest.data, price, p.increment, leading, heldNow, p.viewer.debitsWallet, p.viewer.walletBalance]);

  // The countdown reached zero: the server says whether the auction has started / is over (a late bid may have extended it); the page
  // never decides that from the browser clock.
  // ponytail: one refetch a second later (so the server's clock has passed the instant too); if that still misses, the hub's
  // auction.started / auction.ended event (sent within a few seconds) refetches again.
  const zeroTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const atZero = useCallback(() => {
    clearTimeout(zeroTimer.current);
    zeroTimer.current = setTimeout(() => router.refresh(), 1000);
  }, [router]);
  useEffect(() => () => clearTimeout(zeroTimer.current), []);

  const value = Number(amount);
  const validNumber = amount.trim() !== "" && Number.isFinite(value) && /^\d+(\.\d{1,2})?$/.test(amount.trim());
  const step = (dir: 1 | -1) => {
    const base = validNumber ? cents(value) : cents(minimum);
    const next = Math.max(cents(minimum), base + dir * cents(p.increment));
    setAmount(String(fromCents(next)));
    setError(null);
  };

  const wallet = walletOverride ?? p.viewer.walletBalance ?? 0;
  const lacksInsurance = !!user && (p.viewer.debitsWallet ? wallet + heldNow * (leading ? 1 : 0) : wallet) < p.insurance;
  // With wallet holds on, a bid RESERVES its amount (the balance is untouched; it is charged only if you win); raising your own bid reserves only the difference (what it already holds).
  const held = leading ? heldNow : 0;
  const charge = (n: number) => Math.max(0, fromCents(cents(n) - cents(held)));
  // A low balance only warns: the form stays usable and the server's answer (bid.insufficient_balance) decides.
  const lowBalance = !!user && p.viewer.debitsWallet && wallet < charge(minimum);
  const blocked = !user ? "signIn" : p.viewer.isOwner ? "own" : user.role === "Admin" ? "admin" : phase === "Upcoming" ? "upcoming" : phase === "Ended" ? "ended" : lacksInsurance ? "insurance" : null;
  // The sale, as the server states it. A cancelled sale makes no claim that the viewer won (the server tells only the winning bidder about it).
  const won = phase === "Ended" && p.viewer.isWinner;
  const cancelled = phase === "Ended" && p.viewer.saleStatus === "Voided";
  const saleLine = won ? { Paid: t("salePaid"), Pending: t("salePending"), PaymentFailed: t("salePaymentFailed") }[p.viewer.saleStatus ?? ""] : undefined;

  const review = () => {
    if (!validNumber) return setError(t("invalid"));
    if (cents(value) < cents(minimum)) return setError(t("belowMin", { minimum: money(minimum, locale) }));
    if (!accepted) return setError(t("termsRequired"));
    setError(null);
    setConfirming(true);
  };

  const place = async () => {
    setBusy(true);
    const slowTimer = setTimeout(() => setSlow(true), 10_000);
    try {
      let r: { currentPrice: number; minimumNextBid: number; bidCount: number; availableAfter?: number | null };
      try {
        r = await call(() => api.POST("/api/auctions/{id}/bids", { params: { path: { id: p.auctionId } }, body: { amount: value }, signal: AbortSignal.timeout(30_000) }));
      } catch (e) {
        if (e instanceof ApiError) throw e;
        // No answer (network error or timeout): the bid may still have been placed, and a bid is never sent twice. Look at the auction:
        // if the newest bid is mine at this amount, it was.
        const bids = await call(() => api.GET("/api/auctions/{id}/bids", { params: { path: { id: p.auctionId }, query: { page: 1 } } })).catch(() => null);
        const top = bids?.items[0];
        if (!bids || !top?.isMine || cents(top.amount) !== cents(value)) {
          setError(t("unconfirmed"));
          toast.error(t("unconfirmed"));
          void qc.invalidateQueries({ queryKey: ["auction-bids", p.auctionId] });
          return;
        }
        r = { currentPrice: top.amount, minimumNextBid: fromCents(cents(top.amount) + cents(p.increment)), bidCount: bids.total, availableAfter: Math.max(0, fromCents(cents(wallet) - cents(charge(value)))) };
      }
      setPrice(r.currentPrice);
      setMinimum(r.minimumNextBid);
      setCount(r.bidCount);
      setLeading(true);
      if (p.viewer.debitsWallet) {
        setWalletOverride(r.availableAfter ?? null);
        setHeldNow(value);
      }
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
      clearTimeout(slowTimer);
      setSlow(false);
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
              <Countdown target={p.startsAtUtc} serverNow={p.serverNow} mode="start" size="lg" onDone={atZero} />
            </>
          )}
          {phase === "Live" && <Countdown target={endsAt} serverNow={p.serverNow} size="lg" onDone={atZero} />}
          {phase === "Ended" && <p className="font-semibold text-ink-2">{t("ended")}</p>}
        </div>
      </div>

      <div className="space-y-4 px-5 py-5">
        {won && (
          <Alert tone={p.viewer.saleStatus === "PaymentFailed" ? "warn" : "ok"}>
            <span className="inline-flex items-center gap-2 font-medium">
              <Trophy className="h-4 w-4" aria-hidden /> {t("won")}
            </span>
            {saleLine && <p className="mt-1">{saleLine}</p>}
            {p.viewer.saleStatus === "PaymentFailed" && (
              <Link href="/account/wallet" className="mt-2 inline-block font-semibold underline">
                {t("topUp")}
              </Link>
            )}
          </Alert>
        )}
        {cancelled && (
          <Alert tone="danger">
            <span className="inline-flex items-center gap-2 font-medium">
              <Ban className="h-4 w-4" aria-hidden /> {t("saleVoided")}
            </span>
          </Alert>
        )}
        {leading && phase !== "Upcoming" && !won && !cancelled && (
          <Alert tone="ok">
            <span className="inline-flex items-center gap-2 font-medium">
              <Trophy className="h-4 w-4" aria-hidden /> {t("leading")}
            </span>
            {held > 0 && <p className="tabular mt-1">{t("held", { amount: money(held, locale) })}</p>}
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
              <span className="text-muted">{p.viewer.debitsWallet ? t("available") : t("wallet")}</span>
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
            {lowBalance && (
              <p className="text-sm font-medium text-orange-ink">
                {t("lowBalance")}{" "}
                <Link href="/account/wallet" className="font-semibold underline">
                  {t("topUp")}
                </Link>
              </p>
            )}
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
        {p.viewer.debitsWallet && validNumber && (
          <p className="mt-2 text-sm font-medium">
            {t("confirmCharge", { charge: money(charge(value), locale), remaining: money(Math.max(0, fromCents(cents(wallet) - cents(charge(value)))), locale) })}
          </p>
        )}
        {slow && (
          <p role="status" className="mt-2 text-sm font-medium text-orange-ink">
            {t("slow")}
          </p>
        )}
      </Dialog>
    </section>
  );
}
