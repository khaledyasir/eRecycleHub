"use client";

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

const pad = (n: number) => String(n).padStart(2, "0");

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
  size?: "sm" | "lg";
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

  if (done) return <span className={cn("text-sm font-medium text-muted", className)}>{mode === "end" ? t("ended") : ""}</span>;

  const p = parts(remaining);
  const soon = mode === "end" && remaining < 60 * 60 * 1000;
  const label = `${mode === "end" ? t("endsIn") : t("startsIn")}: ${t("label", p)}`;

  if (size === "lg") {
    const cells = [
      [p.days, t("d")],
      [p.hours, t("h")],
      [p.minutes, t("m")],
    ] as const;
    return (
      <div role="timer" aria-label={label} className={cn("flex gap-2", className)} dir="ltr">
        {cells.map(([v, unit], i) => (
          <div key={i} className={cn("flex min-w-14 flex-col items-center rounded-md px-2 py-1.5", soon ? "bg-rust text-white" : "bg-patina text-white")}>
            <span className="tabular text-2xl font-semibold leading-none">{pad(v)}</span>
            <span className="mt-1 text-[11px] text-white/70">{unit}</span>
          </div>
        ))}
      </div>
    );
  }

  return (
    <span role="timer" aria-label={label} className={cn("tabular inline-flex items-baseline gap-1 font-extrabold text-orange-ink", soon && "text-rust", className)}>
      <span className="sr-only">{mode === "end" ? t("endsIn") : t("startsIn")}</span>
      <span dir="ltr" aria-hidden>
        {p.days > 0 && `${p.days}${t("d")} `}
        {pad(p.hours)}:{pad(p.minutes)}
      </span>
    </span>
  );
}
