"use client";

import { Dialog as D, DropdownMenu as M } from "radix-ui";
import { X } from "lucide-react";
import { useState, type ReactNode } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/format";
import { Button } from "./button";

export function Dialog({
  open,
  onOpenChange,
  trigger,
  title,
  description,
  children,
  footer,
  size = "md",
}: {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  trigger?: ReactNode;
  title: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  footer?: ReactNode;
  size?: "sm" | "md" | "lg";
}) {
  const t = useTranslations("common");
  return (
    <D.Root open={open} onOpenChange={onOpenChange}>
      {trigger && <D.Trigger asChild>{trigger}</D.Trigger>}
      <D.Portal>
        <D.Overlay className="fixed inset-0 z-50 bg-ink/50 data-[state=open]:animate-in" />
        <D.Content
          className={cn(
            "fixed inset-x-3 top-[6vh] z-50 mx-auto flex max-h-[88vh] flex-col overflow-hidden rounded-xl bg-surface shadow-pop focus:outline-none",
            { sm: "max-w-sm", md: "max-w-lg", lg: "max-w-3xl" }[size],
          )}
        >
          <div className="flex items-start justify-between gap-4 border-b border-line px-5 py-4">
            <div>
              <D.Title className="text-lg font-semibold">{title}</D.Title>
              {description ? <D.Description className="mt-1 text-sm text-muted">{description}</D.Description> : <D.Description className="sr-only">{title}</D.Description>}
            </div>
            <D.Close className="rounded p-1 text-muted hover:bg-sunken" aria-label={t("close")}>
              <X className="h-5 w-5" />
            </D.Close>
          </div>
          {children && <div className="overflow-y-auto px-5 py-4">{children}</div>}
          {footer && <div className="flex flex-wrap justify-end gap-2 border-t border-line px-5 py-3">{footer}</div>}
        </D.Content>
      </D.Portal>
    </D.Root>
  );
}

/** Confirmation for destructive or money-moving actions. The confirm button repeats the action's name. */
export function ConfirmDialog({
  trigger,
  title,
  body,
  confirmLabel,
  tone = "danger",
  onConfirm,
  open: controlledOpen,
  onOpenChange,
}: {
  trigger?: ReactNode;
  title: string;
  body?: ReactNode;
  confirmLabel: string;
  tone?: "danger" | "primary";
  onConfirm: () => Promise<unknown> | unknown;
  /** Controlled mode (e.g. opened from a menu item, which closes before a nested dialog could mount). */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}) {
  const t = useTranslations("common");
  const [innerOpen, setInnerOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const open = controlledOpen ?? innerOpen;
  const setOpen = (o: boolean) => (onOpenChange ? onOpenChange(o) : setInnerOpen(o));
  return (
    <Dialog
      open={open}
      onOpenChange={(o) => !busy && setOpen(o)}
      trigger={trigger}
      title={title}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={() => setOpen(false)} disabled={busy}>
            {t("cancel")}
          </Button>
          <Button
            variant={tone}
            loading={busy}
            onClick={async () => {
              setBusy(true);
              try {
                await onConfirm();
                setOpen(false);
              } catch {
                // The caller reports the error (toast); keep the dialog open so the user can retry or cancel.
              } finally {
                setBusy(false);
              }
            }}
          >
            {confirmLabel}
          </Button>
        </>
      }
    >
      {body && <div className="text-sm text-ink-2">{body}</div>}
    </Dialog>
  );
}

export function Menu({ trigger, children, align = "end" }: { trigger: ReactNode; children: ReactNode; align?: "start" | "end" }) {
  return (
    <M.Root>
      <M.Trigger asChild>{trigger}</M.Trigger>
      <M.Portal>
        <M.Content align={align} sideOffset={6} className="z-50 min-w-48 rounded-lg border border-line bg-surface p-1 shadow-pop">
          {children}
        </M.Content>
      </M.Portal>
    </M.Root>
  );
}

export function MenuItem({ children, onSelect, asChild, danger }: { children: ReactNode; onSelect?: () => void; asChild?: boolean; danger?: boolean }) {
  return (
    <M.Item
      asChild={asChild}
      onSelect={onSelect}
      className={cn("flex cursor-pointer select-none items-center gap-2 rounded-md px-3 py-2 text-sm outline-none data-[highlighted]:bg-sunken", danger && "text-rust")}
    >
      {children}
    </M.Item>
  );
}

export const MenuSeparator = () => <M.Separator className="my-1 h-px bg-line" />;
export const MenuLabel = ({ children }: { children: ReactNode }) => <M.Label className="px-3 py-1.5 text-xs text-muted">{children}</M.Label>;
