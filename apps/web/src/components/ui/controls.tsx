"use client";

import { Switch as S, Tabs as T } from "radix-ui";
import type { ReactNode } from "react";
import { cn } from "@/lib/format";

export function Switch({ checked, onCheckedChange, label, disabled, id }: { checked: boolean; onCheckedChange: (v: boolean) => void; label: string; disabled?: boolean; id?: string }) {
  // The wrapper clips Radix's hidden form input (translated -100%), which otherwise pokes out of the page in RTL.
  return (
    <span className="relative -m-1 inline-flex overflow-hidden p-1">
    <S.Root
      id={id}
      checked={checked}
      onCheckedChange={onCheckedChange}
      disabled={disabled}
      aria-label={label}
      className="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full bg-line-strong transition-colors data-[state=checked]:bg-patina disabled:opacity-50"
    >
      <S.Thumb className="block h-5 w-5 translate-x-0.5 rounded-full bg-white shadow transition-transform data-[state=checked]:translate-x-[22px] rtl:-translate-x-0.5 rtl:data-[state=checked]:-translate-x-[22px]" />
    </S.Root>
    </span>
  );
}

export function Tabs({ value, onValueChange, defaultValue, items, className }: {
  value?: string;
  onValueChange?: (v: string) => void;
  defaultValue?: string;
  items: { value: string; label: ReactNode; content: ReactNode }[];
  className?: string;
}) {
  return (
    <T.Root value={value} onValueChange={onValueChange} defaultValue={defaultValue ?? items[0]?.value} className={className}>
      <T.List className="flex gap-1 overflow-x-auto border-b border-line">
        {items.map((i) => (
          <T.Trigger
            key={i.value}
            value={i.value}
            className="-mb-px shrink-0 border-b-2 border-transparent px-3 py-2.5 text-sm font-medium text-muted hover:text-ink data-[state=active]:border-patina data-[state=active]:text-ink"
          >
            {i.label}
          </T.Trigger>
        ))}
      </T.List>
      {items.map((i) => (
        <T.Content key={i.value} value={i.value} className="pt-5 focus-visible:outline-none">
          {i.content}
        </T.Content>
      ))}
    </T.Root>
  );
}

/** Responsive table: scrolls horizontally inside its own box on narrow screens instead of the page. */
export function Table({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cn("relative overflow-x-auto rounded-lg border border-line bg-surface", className)}>
      <table className="w-full min-w-[40rem] border-collapse text-sm [&_td]:border-t [&_td]:border-line [&_td]:px-3 [&_td]:py-2.5 [&_th]:bg-sunken/60 [&_th]:px-3 [&_th]:py-2 [&_th]:text-start [&_th]:text-xs [&_th]:font-semibold [&_th]:text-ink-2">
        {children}
      </table>
    </div>
  );
}
