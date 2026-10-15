import { cn } from "@/lib/format";

/** The eRecycleHUB logo. On dark backgrounds it sits on a white tile so the artwork stays legible. */
export function Logo({ className, inverted }: { className?: string; inverted?: boolean }) {
  return (
    // eslint-disable-next-line @next/next/no-img-element -- static brand asset served from /public
    <img
      src="/erecyclehub-logo.png"
      alt="eRecycleHUB"
      className={cn("h-14 w-auto max-w-[190px] object-contain sm:h-16", inverted && "h-16 rounded-xl bg-white px-2 py-1 sm:h-16", className)}
    />
  );
}
