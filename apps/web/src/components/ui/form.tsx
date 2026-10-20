"use client";

import { Paperclip, X } from "lucide-react";
import { forwardRef, useId, useState, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/format";

const control =
  "w-full rounded-md border border-line-strong bg-surface px-3 text-ink placeholder:text-muted transition-colors focus:border-patina-500 focus:outline-none focus:ring-2 focus:ring-patina-100 disabled:bg-sunken disabled:text-muted aria-[invalid=true]:border-rust aria-[invalid=true]:ring-rust-soft";

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(function Input({ className, ...p }, ref) {
  return <input ref={ref} className={cn(control, "h-10", className)} {...p} />;
});

/** Jordan flag, drawn inline so it renders the same everywhere (flag emoji don't on Windows). */
function JordanFlag() {
  return (
    <svg viewBox="0 0 30 20" className="h-4 w-6 shrink-0 rounded-[2px] shadow-[0_0_0_1px_#0001]" aria-hidden>
      <rect width="30" height="6.67" fill="#111" />
      <rect y="6.67" width="30" height="6.67" fill="#fff" />
      <rect y="13.33" width="30" height="6.67" fill="#007a3d" />
      <path d="M0 0 14 10 0 20Z" fill="#ce1126" />
    </svg>
  );
}

/**
 * Jordanian mobile number as one field: flag and +962 are fixed on the start side (right in Arabic), the user types the
 * number as 07XXXXXXXX. Only digits are kept, at most 10.
 */
export const PhoneInput = forwardRef<HTMLInputElement, Omit<InputHTMLAttributes<HTMLInputElement>, "type" | "dir">>(function PhoneInput({ onChange, className, ...p }, ref) {
  return (
    <div className="flex items-stretch">
      <span className="inline-flex h-10 shrink-0 items-center gap-1.5 rounded-s-md border border-e-0 border-line-strong bg-sunken px-3 text-sm font-medium text-ink-2" dir="ltr">
        <JordanFlag /> +962
      </span>
      <input
        ref={ref}
        type="tel"
        dir="ltr"
        inputMode="numeric"
        autoComplete="tel-national"
        maxLength={10}
        placeholder="07XXXXXXXX"
        className={cn(control, "h-10 rounded-s-none text-start", className)}
        onChange={(e) => {
          e.target.value = e.target.value.replace(/\D/g, "").slice(0, 10);
          onChange?.(e);
        }}
        {...p}
      />
    </div>
  );
});

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(function Textarea({ className, ...p }, ref) {
  return <textarea ref={ref} className={cn(control, "min-h-24 py-2", className)} {...p} />;
});

export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>(function Select({ className, children, ...p }, ref) {
  return (
    <select ref={ref} className={cn(control, "h-10 pe-8", className)} {...p}>
      {children}
    </select>
  );
});

export function Label({ htmlFor, children, required, className }: { htmlFor?: string; children: ReactNode; required?: boolean; className?: string }) {
  return (
    <label htmlFor={htmlFor} className={cn("text-sm font-medium text-ink", className)}>
      {children}
      {required && (
        <span className="text-rust" aria-hidden>
          {" "}
          *
        </span>
      )}
    </label>
  );
}

/**
 * Label + control + hint + error, wired with aria-describedby / aria-invalid.
 * `children` receives the generated id and aria props to spread on the control.
 */
export function Field({
  label,
  hint,
  error,
  required,
  className,
  children,
}: {
  label: ReactNode;
  hint?: ReactNode;
  error?: string;
  required?: boolean;
  className?: string;
  children: (props: { id: string; "aria-invalid"?: boolean; "aria-describedby"?: string; required?: boolean }) => ReactNode;
}) {
  const id = useId();
  const describedBy = [hint ? `${id}-hint` : null, error ? `${id}-error` : null].filter(Boolean).join(" ") || undefined;
  return (
    <div className={cn("flex flex-col gap-1.5", className)}>
      <Label htmlFor={id} required={required}>
        {label}
      </Label>
      {children({ id, "aria-invalid": error ? true : undefined, "aria-describedby": describedBy, required })}
      {hint && !error && (
        <p id={`${id}-hint`} className="text-xs text-muted">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className="text-xs font-medium text-rust" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

export const Checkbox = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement> & { label: ReactNode }>(function Checkbox({ label, className, ...p }, ref) {
  return (
    <label className={cn("inline-flex cursor-pointer items-center gap-2 text-sm", className)}>
      <input ref={ref} type="checkbox" className="h-4 w-4 rounded-sm border-line-strong accent-patina" {...p} />
      <span>{label}</span>
    </label>
  );
});

/**
 * File picker with a label in the UI language (native pickers say "Choose file" in the browser's language — a legacy complaint).
 * Shows the current file name (existing upload) and the chosen one.
 */
export function FileInput({
  id,
  accept,
  multiple,
  onFiles,
  current,
  onClear,
  ...aria
}: {
  id?: string;
  accept?: string;
  multiple?: boolean;
  onFiles: (files: File[]) => void;
  current?: string | null;
  onClear?: () => void;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
}) {
  const t = useTranslations("common.file");
  const [names, setNames] = useState<string[]>([]);
  const fallbackId = useId();
  const inputId = id ?? fallbackId;
  const shown = names.length ? names.join(", ") : current;
  return (
    <div className="flex flex-wrap items-center gap-2">
      <input
        id={inputId}
        type="file"
        accept={accept}
        multiple={multiple}
        className="peer sr-only"
        onChange={(e) => {
          const list = Array.from(e.target.files ?? []);
          setNames(list.map((f) => f.name));
          onFiles(list);
        }}
        {...aria}
      />
      <label
        htmlFor={inputId}
        className="inline-flex h-10 cursor-pointer items-center gap-2 rounded-md border border-dashed border-line-strong bg-surface px-3 text-sm text-ink-2 hover:bg-sunken peer-focus-visible:outline-2 peer-focus-visible:outline-patina-500"
      >
        <Paperclip className="h-4 w-4" aria-hidden />
        {multiple ? t("chooseMany") : t("choose")}
      </label>
      <span className="min-w-0 truncate text-sm text-muted">{shown || t("none")}</span>
      {shown && onClear && (
        <button
          type="button"
          onClick={() => {
            setNames([]);
            onClear();
          }}
          className="rounded p-1 text-muted hover:bg-sunken hover:text-rust"
          aria-label={t("remove")}
        >
          <X className="h-4 w-4" />
        </button>
      )}
    </div>
  );
}
