"use client";

import { Star } from "lucide-react";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Textarea } from "@/components/ui/form";
import { useSession } from "@/components/providers";
import { Link, useRouter } from "@/i18n/navigation";
import { api, call } from "@/lib/api/client";
import { cn } from "@/lib/format";
import { useErrorMessage } from "@/lib/use-error-message";

export function ReviewForm({ auctionId, hasReviewed }: { auctionId: number; hasReviewed: boolean }) {
  const t = useTranslations("auction.reviews");
  const te = useTranslations("errors");
  const user = useSession();
  const router = useRouter();
  const message = useErrorMessage();
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(hasReviewed);

  if (!user)
    return (
      <p className="text-sm text-muted">
        <Link href={`/login?next=${encodeURIComponent(`/auctions/${auctionId}`)}`} className="font-medium text-patina underline">
          {t("signIn")}
        </Link>
      </p>
    );
  if (done) return <Alert tone="ok">{t("already")}</Alert>;

  return (
    <form
      className="space-y-3 rounded-lg border border-line bg-surface p-4"
      onSubmit={async (e) => {
        e.preventDefault();
        if (rating < 1) return setError(te("review.invalid_rating"));
        setBusy(true);
        setError(null);
        try {
          await call(() => api.POST("/api/auctions/{id}/reviews", { params: { path: { id: auctionId } }, body: { rating, comment: comment.trim() || null } }));
          toast.success(t("posted"));
          setDone(true);
          router.refresh();
        } catch (err) {
          setError(message(err));
        } finally {
          setBusy(false);
        }
      }}
    >
      <h3 className="font-semibold">{t("write")}</h3>
      <fieldset>
        <legend className="mb-1 text-sm font-medium">{t("rating")}</legend>
        <div className="flex gap-1" role="radiogroup">
          {[1, 2, 3, 4, 5].map((n) => (
            <label key={n} className="cursor-pointer" title={t("stars", { n })}>
              <input type="radio" name="rating" value={n} checked={rating === n} onChange={() => setRating(n)} className="peer sr-only" />
              <span className="sr-only">{t("stars", { n })}</span>
              <Star className={cn("h-7 w-7 rounded peer-focus-visible:outline-2 peer-focus-visible:outline-patina-500", n <= rating ? "fill-hivis text-hivis" : "text-line-strong")} aria-hidden />
            </label>
          ))}
        </div>
      </fieldset>
      <div>
        <label htmlFor="review-comment" className="mb-1 block text-sm font-medium">
          {t("comment")}
        </label>
        <Textarea id="review-comment" maxLength={2000} value={comment} onChange={(e) => setComment(e.target.value)} aria-describedby="review-hint" />
        <p id="review-hint" className="mt-1 text-xs text-muted">
          {t("commentHint")}
        </p>
      </div>
      {error && <p role="alert" className="text-sm font-medium text-rust">{error}</p>}
      <Button type="submit" loading={busy}>
        {t("submit")}
      </Button>
    </form>
  );
}
