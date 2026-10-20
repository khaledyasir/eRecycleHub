"use client";

import { Clock } from "lucide-react";
import { useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { cn } from "@/lib/format";

/**
 * Ticking countdown to an absolute UTC instant. The first render uses the server's clock (`serverNow`) so the server
 * and browser output match; afterwards the browser clock is corrected by the measured skew, so a wrong device clock or
 * time zone cannot show a different remaining time. The server still decides whether a bid is accepted.
 */
export function useServerClock(serverNow: number) {
  const [now, setNow] = useState(serverNow);
  useEffect(() => {
    const skew = serverNow - Date.now();
    const tick = () => setNow(Date.now() + skew);
    tick();
    const id = setInterval(tick, 1000);
    return () => clearInterval(id);
  }, [serverNow]);
  return now;
}

export function parts(ms: number) {
  const s = Math.max(0, Math.floor(ms / 1000));
  return { days: Math.floor(s / 86400), hours: Math.floor((s % 86400) / 3600), minutes: Math.floor((s % 3600) / 60), seconds: s % 60 };
}

/** Only the largest unit that is still non-zero: days while there are any, then hours, then minutes, then seconds. */
export function lead(p: ReturnType<typeof parts>) {
  if (p.days > 0) return { n: p.days, unit: "d" as const };
  if (p.hours > 0) return { n: p.hours, unit: "h" as const };
  if (p.minutes > 0) return { n: p.minutes, unit: "m" as const };
  return { n: p.seconds, unit: "s" as const };
}

export function Countdown({
  target,
  serverNow,
  mode = "end",
  size = "sm",
  className,
  onDone,
}: {
  target: string | null | undefined;
  serverNow: number;
  mode?: "end" | "start";
  size?: "sm" | "lg" | "card";
  className?: string;
  onDone?: () => void;
}) {
  const t = useTranslations("common.countdown");
  const now = useServerClock(serverNow);
  const remaining = target ? new Date(target).getTime() - now : 0;
  const done = remaining <= 0;

  useEffect(() => {
    if (done && onDone) onDone();
  }, [done, onDone]);

  if (done && size === "card") return null;
  if (done) return <span className={cn("text-sm font-medium text-muted", className)}>{mode === "end" ? t("ended") : ""}</span>;

  const p = parts(remaining);
  const soon = mode === "end" && remaining < 60 * 60 * 1000;
  const label = `${mode === "end" ? t("endsIn") : t("startsIn")}: ${t("label", p)}`;

  const { n, unit } = lead(p);

  if (size === "card") {
    // Small card beside the price: "Ends in / 2 days".
    return (
      <div role="timer" aria-label={label} className={cn("flex items-center gap-2 rounded-lg border border-line bg-ground px-2.5 py-1.5", className)}>
        <Clock className={cn("h-4 w-4 shrink-0", soon ? "text-rust" : "text-patina")} aria-hidden />
        <span className="flex flex-col leading-tight" aria-hidden>
          <span className="text-[10px] text-muted">{mode === "end" ? t("endsIn") : t("startsIn")}</span>
          <span className={cn("tabular text-sm font-extrabold", soon ? "text-rust" : "text-ink")}>
            {n} {t(`unit.${unit}`, { n })}
          </span>
        </span>
      </div>
    );
  }

  if (size === "lg") {
    return (
      <div role="timer" aria-label={label} className={cn("flex", className)} dir="ltr">
        <div className={cn("flex min-w-24 flex-col items-center rounded-md px-4 py-2", soon ? "bg-rust text-white" : "bg-patina text-white")}>
          <span className="tabular text-3xl font-semibold leading-none">{n}</span>
          <span className="mt-1 text-xs text-white/80">{t(`unit.${unit}`, { n })}</span>
        </div>
      </div>
    );
  }

  return (
    <span role="timer" aria-label={label} className={cn("tabular inline-flex items-baseline gap-1 font-extrabold text-orange-ink", soon && "text-rust", className)}>
      <span className="sr-only">{mode === "end" ? t("endsIn") : t("startsIn")}</span>
      <bdi aria-hidden>
        {n} {t(unit)}
      </bdi>
    </span>
  );
}
