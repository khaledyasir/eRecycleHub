"use client";

import { ExternalLink, Mail, MoreHorizontal, Pencil, Trash2 } from "lucide-react";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/display";
import { FileInput } from "@/components/ui/form";
import { ConfirmDialog, Menu, MenuItem } from "@/components/ui/overlay";
import { Switch } from "@/components/ui/controls";
import { Link, useRouter } from "@/i18n/navigation";
import { api, call, sendForm, sendJson } from "@/lib/api/client";
import { fileUrl } from "@/lib/format";
import { useErrorMessage } from "@/lib/use-error-message";
import type { ManageBase } from "./auction-form";

/** Runs a mutation with a toast for success or the localised API error, then refreshes server data. */
export function useAction() {
  const router = useRouter();
  const message = useErrorMessage();
  return async (fn: () => Promise<unknown>, success?: string) => {
    try {
      const r = await fn();
      if (success) toast.success(success);
      router.refresh();
      return r;
    } catch (e) {
      toast.error(message(e));
      throw e;
    }
  };
}

export function ActiveSwitch({ base, id, active, label }: { base: ManageBase; id: number; active: boolean; label: string }) {
  const t = useTranslations("seller.manage");
  const run = useAction();
  const [on, setOn] = useState(active);
  const [busy, setBusy] = useState(false);
  return (
    <Switch
      checked={on}
      disabled={busy}
      label={`${label}: ${on ? t("deactivate") : t("activate")}`}
      onCheckedChange={async (next) => {
        setBusy(true);
        setOn(next);
        try {
          await run(() => sendJson("POST", `${base}/auctions/${id}/active`, { active: next }), next ? t("activated") : t("deactivated"));
        } catch {
          setOn(!next);
        } finally {
          setBusy(false);
        }
      }}
    />
  );
}

export function RowActions({ base, id, routeBase, title }: { base: ManageBase; id: number; routeBase: string; title: string }) {
  const t = useTranslations("seller.manage");
  const run = useAction();
  const [confirm, setConfirm] = useState(false);
  return (
    <>
      <Menu trigger={<Button variant="ghost" size="icon" aria-label={`${t("actions")}: ${title}`}><MoreHorizontal className="h-4 w-4" /></Button>}>
        <MenuItem asChild>
          <Link href={`${routeBase}/${id}`}><Pencil className="h-4 w-4" /> {t("edit")}</Link>
        </MenuItem>
        <MenuItem asChild>
          <Link href={`/auctions/${id}`} target="_blank"><ExternalLink className="h-4 w-4" /> {t("view")}</Link>
        </MenuItem>
        <MenuItem danger onSelect={() => setConfirm(true)}>
          <Trash2 className="h-4 w-4" /> {t("delete")}
        </MenuItem>
      </Menu>
      <ConfirmDialog
        open={confirm}
        onOpenChange={setConfirm}
        title={t("deleteTitle")}
        body={t("deleteBody")}
        confirmLabel={t("delete")}
        onConfirm={() => run(() => sendJson("DELETE", `${base}/auctions/${id}`), t("deleted"))}
      />
    </>
  );
}

export function ImageManager({ base, auctionId, images }: { base: ManageBase; auctionId: number; images: { id: number; fileId: string; title?: string | null }[] }) {
  const t = useTranslations("seller.manage.photos");
  const run = useAction();
  const [files, setFiles] = useState<File[]>([]);
  const [busy, setBusy] = useState(false);
  const [key, setKey] = useState(0);
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-3">
        <FileInput key={key} multiple accept="image/jpeg,image/png,image/webp" onFiles={setFiles} />
        <Button
          disabled={files.length === 0}
          loading={busy}
          onClick={async () => {
            setBusy(true);
            try {
              await run(() => sendForm("POST", `${base}/auctions/${auctionId}/images`, {}, { images: files }), t("uploaded", { count: files.length }));
              setFiles([]);
              setKey((k) => k + 1);
            } catch {
              /* toast shown by useAction */
            } finally {
              setBusy(false);
            }
          }}
        >
          {t("upload")}
        </Button>
      </div>
      {images.length === 0 ? (
        <EmptyState title={t("empty")} />
      ) : (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {images.map((img) => (
            <li key={img.id} className="group relative overflow-hidden rounded-lg border border-line bg-sunken">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={fileUrl(img.fileId)} alt={img.title ?? ""} className="aspect-[4/3] w-full object-cover" />
              <div className="absolute end-2 top-2">
                <ConfirmDialog
                  trigger={<Button size="icon" variant="secondary" aria-label={t("delete")}><Trash2 className="h-4 w-4 text-rust" /></Button>}
                  title={t("deleteTitle")}
                  confirmLabel={t("delete")}
                  onConfirm={() => run(() => sendJson("DELETE", `${base}/auctions/${auctionId}/images/${img.id}`), t("deleted"))}
                />
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function NotifyWinnerButton({ auctionId }: { auctionId: number }) {
  const t = useTranslations("seller.manage");
  const run = useAction();
  const [busy, setBusy] = useState(false);
  return (
    <Button
      variant="secondary"
      loading={busy}
      onClick={async () => {
        setBusy(true);
        try {
          const r = (await run(() => call(() => api.POST("/api/admin/auctions/{id}/notify-winner", { params: { path: { id: auctionId } } })))) as { sent: number };
          toast.success(t("notified", { sent: r.sent }));
        } catch {
          /* toast shown */
        } finally {
          setBusy(false);
        }
      }}
    >
      <Mail className="h-4 w-4" aria-hidden /> {t("notify")}
    </Button>
  );
}
