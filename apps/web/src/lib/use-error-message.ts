"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback } from "react";
import { ApiError } from "./api/types";
import { money } from "./format";

const MONEY_ARGS = new Set(["minimum", "insurance", "max"]);

/** Turns any thrown error into a sentence in the UI language: API codes → errors.json, then HTTP status, then generic. */
export function useErrorMessage() {
  const t = useTranslations("errors");
  const locale = useLocale();
  return useCallback(
    (err: unknown): string => {
      if (err instanceof ApiError) {
        const args = Object.fromEntries(
          Object.entries(err.args ?? {}).map(([k, v]) => [k, typeof v === "number" && MONEY_ARGS.has(k) ? money(v, locale) : String(v)]),
        );
        if (t.has(err.code)) return t(err.code, args);
        const status = String(err.status);
        if (t.has(`status.${status}`)) return t(`status.${status}`);
        return t("generic");
      }
      if (err instanceof TypeError) return t("network");
      return t("generic");
    },
    [t, locale],
  );
}
