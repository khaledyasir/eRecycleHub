"use client";

import { AlertTriangle } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";

export default function ErrorPage({ error, reset }: { error: Error & { digest?: string; status?: number }; reset: () => void }) {
  const t = useTranslations("common");
  const forbidden = error.message === "auth.forbidden" || error.status === 403;
  return (
    <div className="container-page flex min-h-[50vh] flex-col items-start justify-center gap-4 py-16" role="alert">
      <AlertTriangle className="h-10 w-10 text-rust" aria-hidden />
      <h1 className="text-2xl font-semibold">{t("error.title")}</h1>
      <p className="text-muted">{forbidden ? t("error.forbidden") : t("error.body")}</p>
      {!forbidden && <Button onClick={reset}>{t("retry")}</Button>}
    </div>
  );
}
