import { cn } from "@/lib/format";

/**
 * Interim eRecycleHub wordmark. The legacy repository has no eRecycleHub logo (its logo files belong to other
 * projects: "Super Deals", "PROBID", "hsERP"), so this mark stands in until the owner supplies the brand asset.
 */
export function Logo({ className, inverted }: { className?: string; inverted?: boolean }) {
  return (
    <span className={cn("inline-flex items-center gap-2 font-semibold tracking-tight", inverted ? "text-white" : "text-ink", className)} dir="ltr">
      <svg viewBox="0 0 32 32" className="h-8 w-8 shrink-0" aria-hidden>
        <rect width="32" height="32" rx="7" fill={inverted ? "#ffffff" : "#0e6b57"} />
        <path d="M16 7.5a8.5 8.5 0 1 1-7.6 4.7" fill="none" stroke={inverted ? "#0e6b57" : "#ffffff"} strokeWidth="2.6" strokeLinecap="round" />
        <path d="M6.2 9.4l2.4 3.4 3.6-2.2" fill="none" stroke={inverted ? "#0e6b57" : "#ffffff"} strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" />
        <circle cx="16" cy="16" r="3" fill="#f2b705" />
      </svg>
      <span className="text-lg leading-none">
        e<span className={inverted ? "text-white" : "text-patina"}>Recycle</span>Hub
      </span>
    </span>
  );
}
