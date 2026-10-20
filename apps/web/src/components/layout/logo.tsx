import { cn } from "@/lib/format";

/** The eRecycleHUB logo (transparent PNG, so it can sit directly on the dark footer). */
export function Logo({ className, inverted }: { className?: string; inverted?: boolean }) {
  return (
    // eslint-disable-next-line @next/next/no-img-element -- static brand asset served from /public
    <img
      src="/erecyclehub-logo.png"
      alt="eRecycleHUB"
      className={cn("h-14 w-auto max-w-[190px] object-contain sm:h-16", inverted && "h-20 sm:h-20", className)}
    />
  );
}
