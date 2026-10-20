"use client";

import { Store } from "lucide-react";
import { useTranslations } from "next-intl";
import { useState, useSyncExternalStore } from "react";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/overlay";
import { Link } from "@/i18n/navigation";

/** Set by the sign-up form so the first visit to the account page asks once (and only once) whether the new user wants to sell. */
export const WELCOME_KEY = "erh_welcome";

const noSubscribe = () => () => {};
const flagged = () => {
  try {
    return !!localStorage.getItem(WELCOME_KEY);
  } catch {
    return false; // storage blocked: skip the prompt
  }
};

export function WelcomePrompt() {
  const t = useTranslations("account.welcome");
  const wanted = useSyncExternalStore(noSubscribe, flagged, () => false);
  const [dismissed, setDismissed] = useState(false);
  const close = () => {
    setDismissed(true);
    try {
      localStorage.removeItem(WELCOME_KEY);
    } catch {
      /* ignore */
    }
  };
  return (
    <Dialog
      open={wanted && !dismissed}
      onOpenChange={(v) => !v && close()}
      size="sm"
      title={t("title")}
      description={t("body")}
      footer={
        <>
          <Button variant="secondary" onClick={close}>{t("later")}</Button>
          <Link href="/account/become-seller" onClick={close} className="inline-flex h-11 items-center gap-2 rounded-md bg-patina px-5 text-sm font-bold text-white hover:bg-patina-700">
            <Store className="h-4 w-4" aria-hidden /> {t("yes")}
          </Link>
        </>
      }
    >
      <p className="text-sm text-ink-2">{t("hint")}</p>
    </Dialog>
  );
}
