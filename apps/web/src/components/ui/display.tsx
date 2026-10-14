import type { ReactNode } from "react";
import { cn } from "@/lib/format";

const tones = {
  neutral: "bg-sunken text-ink-2",
  live: "bg-hivis text-hivis-ink",
  upcoming: "bg-info-soft text-info",
  ended: "bg-sunken text-ink-2 ring-1 ring-inset ring-line-strong",
  ok: "bg-ok-soft text-ok",
  warn: "bg-hivis-soft text-hivis-ink",
  danger: "bg-rust-soft text-rust",
  brand: "bg-patina-50 text-patina-700",
} as const;

export type Tone = keyof typeof tones;

export function Badge({ tone = "neutral", children, className }: { tone?: Tone; children: ReactNode; className?: string }) {
  return <span className={cn("inline-flex items-center gap-1 rounded-sm px-2 py-0.5 text-xs font-semibold", tones[tone], className)}>{children}</span>;
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={cn("animate-pulse rounded-md bg-sunken", className)} aria-hidden />;
}

/** An empty screen is an invitation to act: say what is missing and offer the next step. */
export function EmptyState({ icon, title, body, action, className }: { icon?: ReactNode; title: string; body?: string; action?: ReactNode; className?: string }) {
  return (
    <div className={cn("flex flex-col items-center gap-3 rounded-lg border border-dashed border-line-strong bg-surface px-6 py-12 text-center", className)}>
      {icon && <div className="text-muted">{icon}</div>}
      <p className="font-semibold text-ink">{title}</p>
      {body && <p className="max-w-md text-sm text-muted">{body}</p>}
      {action}
    </div>
  );
}

export function Panel({ title, actions, children, className, bodyClassName }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string; bodyClassName?: string }) {
  return (
    <section className={cn("min-w-0 rounded-lg border border-line bg-surface", className)}>
      {(title || actions) && (
        <header className="flex flex-wrap items-center justify-between gap-2 border-b border-line px-4 py-3">
          {title && <h2 className="text-base font-semibold">{title}</h2>}
          {actions}
        </header>
      )}
      <div className={cn("p-4", bodyClassName)}>{children}</div>
    </section>
  );
}

export function PageHeader({ title, description, actions }: { title: ReactNode; description?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        <h1 className="text-2xl font-semibold">{title}</h1>
        {description && <p className="mt-1 text-sm text-muted">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  );
}

/** Key figure. `hint` explains the number; no decorative deltas. */
export function Stat({ label, value, hint, tone }: { label: string; value: ReactNode; hint?: ReactNode; tone?: "attention" }) {
  return (
    <div className={cn("rounded-lg border border-line bg-surface p-4", tone === "attention" && "border-hivis bg-hivis-soft")}>
      <p className="text-sm text-muted">{label}</p>
      <p className="tabular mt-1 text-2xl font-semibold">{value}</p>
      {hint && <p className="mt-1 text-xs text-muted">{hint}</p>}
    </div>
  );
}

export function Alert({ tone = "info", title, children }: { tone?: "info" | "danger" | "ok" | "warn"; title?: string; children?: ReactNode }) {
  const cls = { info: "border-info/30 bg-info-soft text-info", danger: "border-rust/30 bg-rust-soft text-rust", ok: "border-ok/30 bg-ok-soft text-ok", warn: "border-hivis bg-hivis-soft text-hivis-ink" }[tone];
  return (
    <div className={cn("rounded-md border px-4 py-3 text-sm", cls)} role={tone === "danger" ? "alert" : "status"}>
      {title && <p className="font-semibold">{title}</p>}
      {children && <div className={cn(title && "mt-1")}>{children}</div>}
    </div>
  );
}
