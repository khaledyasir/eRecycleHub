"use client";

import { ChevronLeft, ChevronRight, ImageOff } from "lucide-react";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { cn, fileUrl } from "@/lib/format";

/** Main photo + thumbnails. Arrow keys move between photos when the gallery has focus. */
export function Gallery({ fileIds, title }: { fileIds: string[]; title: string }) {
  const t = useTranslations("auction.detail");
  const [i, setI] = useState(0);
  if (fileIds.length === 0)
    return (
      <div className="flex aspect-[4/3] items-center justify-center rounded-lg border border-line bg-sunken text-muted">
        <ImageOff className="h-10 w-10" aria-hidden />
      </div>
    );
  const go = (d: number) => setI((x) => (x + d + fileIds.length) % fileIds.length);
  return (
    <section aria-label={t("gallery")}>
      <div
        className="group relative aspect-[4/3] overflow-hidden rounded-lg border border-line bg-sunken"
        tabIndex={0}
        onKeyDown={(e) => {
          const rtl = document.documentElement.dir === "rtl";
          if (e.key === "ArrowRight") go(rtl ? -1 : 1);
          if (e.key === "ArrowLeft") go(rtl ? 1 : -1);
        }}
      >
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src={fileUrl(fileIds[i])} alt={`${title} — ${t("photo", { n: i + 1, total: fileIds.length })}`} className="h-full w-full object-contain" />
        {fileIds.length > 1 && (
          <>
            <button type="button" onClick={() => go(-1)} className="absolute start-2 top-1/2 -translate-y-1/2 rounded-full bg-surface/90 p-2 shadow-raise hover:bg-surface" aria-label={t("previous")}>
              <ChevronLeft className="flip-rtl h-5 w-5" />
            </button>
            <button type="button" onClick={() => go(1)} className="absolute end-2 top-1/2 -translate-y-1/2 rounded-full bg-surface/90 p-2 shadow-raise hover:bg-surface" aria-label={t("next")}>
              <ChevronRight className="flip-rtl h-5 w-5" />
            </button>
            <span className="tabular absolute bottom-2 end-2 rounded-sm bg-ink/80 px-2 py-0.5 text-xs text-white" aria-live="polite">
              {t("photo", { n: i + 1, total: fileIds.length })}
            </span>
          </>
        )}
      </div>
      {fileIds.length > 1 && (
        <ul className="mt-3 flex gap-2 overflow-x-auto pb-1">
          {fileIds.map((id, n) => (
            <li key={id} className="shrink-0">
              <button
                type="button"
                onClick={() => setI(n)}
                aria-current={n === i}
                aria-label={t("photo", { n: n + 1, total: fileIds.length })}
                className={cn("h-16 w-20 overflow-hidden rounded-md border-2", n === i ? "border-patina" : "border-transparent opacity-80 hover:opacity-100")}
              >
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={fileUrl(id)} alt="" loading="lazy" className="h-full w-full object-cover" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
