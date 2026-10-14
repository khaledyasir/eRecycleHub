import { MapPin, Phone, Store } from "lucide-react";
import { useTranslations } from "next-intl";
import { Link } from "@/i18n/navigation";
import type { Schema } from "@/lib/api/types";
import { fileUrl } from "@/lib/format";

export function StoreCard({ s }: { s: Schema<"StoreCard"> }) {
  const t = useTranslations("public.stores");
  return (
    <article className="relative flex flex-col overflow-hidden rounded-lg border border-line bg-surface hover:shadow-raise">
      <div className="h-20 bg-patina-900">
        {s.backgroundFileId && (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={fileUrl(s.backgroundFileId)} alt="" loading="lazy" className="h-full w-full object-cover opacity-90" />
        )}
      </div>
      <div className="-mt-8 flex flex-1 flex-col gap-3 px-4 pb-4">
        <div className="h-16 w-16 overflow-hidden rounded-lg border-4 border-surface bg-sunken">
          {s.logoFileId ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={fileUrl(s.logoFileId)} alt="" loading="lazy" className="h-full w-full object-cover" />
          ) : (
            <Store className="m-auto mt-3 h-8 w-8 text-muted" aria-hidden />
          )}
        </div>
        <div>
          <h3 className="font-semibold">
            <Link href={`/stores/${s.id}`} className="after:absolute after:inset-0">
              {s.name}
            </Link>
          </h3>
          <p className="mt-1 text-sm text-muted">
            <span className="tabular">{t("lots", { count: s.auctionCount })}</span>
            {s.liveCount > 0 && <span className="ms-2 rounded-sm bg-hivis px-1.5 py-0.5 text-xs font-semibold text-hivis-ink">{t("live", { count: s.liveCount })}</span>}
          </p>
        </div>
        <ul className="mt-auto space-y-1 text-sm text-ink-2">
          {s.address && (
            <li className="flex items-start gap-2">
              <MapPin className="mt-0.5 h-4 w-4 shrink-0 text-muted" aria-hidden />
              <span className="line-clamp-1">{s.address}</span>
            </li>
          )}
          {s.phone && (
            <li className="relative z-10 flex items-center gap-2">
              <Phone className="h-4 w-4 shrink-0 text-muted" aria-hidden />
              <a href={`tel:${s.phone.replace(/\s/g, "")}`} className="hover:underline" dir="ltr">
                {s.phone}
              </a>
            </li>
          )}
        </ul>
      </div>
    </article>
  );
}
