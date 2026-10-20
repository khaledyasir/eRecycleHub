"use client";

import { Check, Copy } from "lucide-react";
import { useTranslations } from "next-intl";
import { useState } from "react";

/** Copies a value (an IBAN, a CliQ alias) to the clipboard and confirms for a moment. */
export function CopyButton({ value }: { value: string }) {
  const t = useTranslations("account.wallet");
  const [done, setDone] = useState(false);
  return (
    <button
      type="button"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(value);
          setDone(true);
          setTimeout(() => setDone(false), 1800);
        } catch {
          /* clipboard blocked: the value is still visible to select */
        }
      }}
      className="inline-flex h-8 shrink-0 items-center gap-1.5 rounded-md border border-line-strong bg-surface px-2.5 text-xs font-bold text-ink-2 hover:bg-sunken"
    >
      {done ? <Check className="h-3.5 w-3.5 text-ok" aria-hidden /> : <Copy className="h-3.5 w-3.5" aria-hidden />}
      {done ? t("copied") : t("copy")}
    </button>
  );
}
