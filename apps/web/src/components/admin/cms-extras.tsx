"use client";

import { Check, RotateCcw, Trash2 } from "lucide-react";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { useAction } from "@/components/manage/manage-actions";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/form";
import { ConfirmDialog } from "@/components/ui/overlay";
import { sendJson } from "@/lib/api/client";

export function MessageActions({ id, isRead }: { id: number; isRead: boolean }) {
  const t = useTranslations("cms.messages");
  const tc = useTranslations("cms.common");
  const run = useAction();
  return (
    <div className="flex items-center gap-1">
      {!isRead && (
        <Button variant="secondary" size="sm" onClick={() => run(() => sendJson("POST", `/api/admin/messages/${id}/read`), t("read"))}>
          <Check className="h-4 w-4" aria-hidden /> {t("markRead")}
        </Button>
      )}
      <ConfirmDialog
        trigger={<Button variant="ghost" size="sm" aria-label={t("deleteTitle")}><Trash2 className="h-4 w-4 text-rust" aria-hidden /></Button>}
        title={t("deleteTitle")}
        confirmLabel={tc("delete")}
        onConfirm={() => run(() => sendJson("DELETE", `/api/admin/messages/${id}`), t("deleted"))}
      />
    </div>
  );
}

/** One editable text: saves an override for (locale, key), or removes it again. */
export function TranslationRow({ locale, textKey, shipped, override }: { locale: string; textKey: string; shipped: string; override: string | null }) {
  const t = useTranslations("cms.translations");
  const run = useAction();
  const [value, setValue] = useState(override ?? shipped);
  const dirty = value !== (override ?? shipped);
  const dir = locale === "ar" ? "rtl" : "ltr";
  return (
    <tr>
      <td className="align-top text-xs" dir="ltr">
        <code className="break-all">{textKey}</code>
        {override !== null && <span className="ms-2 rounded bg-hivis/30 px-1.5 py-0.5 text-[11px]">{t("overridden")}</span>}
      </td>
      <td className="max-w-64 align-top text-sm text-ink-2" dir={dir}>{shipped}</td>
      <td className="min-w-64 align-top">
        <label className="sr-only" htmlFor={`tr-${textKey}`}>{t("override")}: {textKey}</label>
        <Textarea id={`tr-${textKey}`} rows={2} value={value} dir={dir} onChange={(e) => setValue(e.target.value)} />
        <div className="mt-1.5 flex gap-2">
          <Button size="sm" disabled={!dirty || !value.trim() || value === shipped} onClick={() => run(() => sendJson("PUT", "/api/admin/translations", { locale, key: textKey, value }), t("saved"))}>
            {t("save")}
          </Button>
          {override !== null && (
            <Button
              size="sm"
              variant="secondary"
              onClick={async () => {
                await run(() => sendJson("DELETE", `/api/admin/translations/${locale}/${textKey}`), t("resetDone"));
                setValue(shipped);
              }}
            >
              <RotateCcw className="h-3.5 w-3.5" aria-hidden /> {t("reset")}
            </Button>
          )}
        </div>
      </td>
    </tr>
  );
}
