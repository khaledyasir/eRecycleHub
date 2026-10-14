"use client";

import { Bold, Heading3, Italic, Link2, List, ListOrdered, RemoveFormatting } from "lucide-react";
import { useEffect, useRef } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/format";

/**
 * Minimal rich-text editor for descriptions and CMS pages (bold, italic, heading, lists, links).
 * The API sanitises whatever it receives, so this only needs to be convenient, not a security boundary.
 */
// ponytail: contentEditable + execCommand (deprecated but universally supported); swap for a structured editor if tables/media are needed.
export function RichTextEditor({ value, onChange, dir, id, "aria-describedby": describedBy, "aria-invalid": invalid, minHeight = 160 }: {
  value: string;
  onChange: (html: string) => void;
  dir?: "ltr" | "rtl";
  id?: string;
  "aria-describedby"?: string;
  "aria-invalid"?: boolean;
  minHeight?: number;
}) {
  const t = useTranslations("common.editor");
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (ref.current && ref.current.innerHTML !== value) ref.current.innerHTML = value ?? "";
  }, [value]);

  const run = (cmd: string, arg?: string) => {
    ref.current?.focus();
    document.execCommand(cmd, false, arg);
    onChange(ref.current?.innerHTML ?? "");
  };

  const tools = [
    { icon: Bold, label: t("bold"), cmd: "bold" },
    { icon: Italic, label: t("italic"), cmd: "italic" },
    { icon: Heading3, label: t("heading"), cmd: "formatBlock", arg: "h3" },
    { icon: List, label: t("bullets"), cmd: "insertUnorderedList" },
    { icon: ListOrdered, label: t("numbers"), cmd: "insertOrderedList" },
    { icon: Link2, label: t("link"), cmd: "createLink" },
    { icon: RemoveFormatting, label: t("clear"), cmd: "removeFormat" },
  ];

  const onTool = (cmd: string, arg?: string) => {
    if (cmd !== "createLink") return run(cmd, arg);
    const url = window.prompt(t("linkPrompt"), "https://");
    if (url && /^(https?:|mailto:)/i.test(url)) run("createLink", url);
  };

  return (
    <div className={cn("rounded-md border border-line-strong bg-surface focus-within:border-patina-500 focus-within:ring-2 focus-within:ring-patina-100", invalid && "border-rust")}>
      <div className="flex flex-wrap gap-0.5 border-b border-line p-1" role="toolbar" aria-label={t("toolbar")}>
        {tools.map(({ icon: Icon, label, cmd, arg }) => (
          <button key={label} type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => onTool(cmd, arg)} className="rounded p-1.5 text-ink-2 hover:bg-sunken" title={label} aria-label={label}>
            <Icon className="h-4 w-4" aria-hidden />
          </button>
        ))}
      </div>
      <div
        ref={ref}
        id={id}
        role="textbox"
        aria-multiline="true"
        aria-describedby={describedBy}
        aria-invalid={invalid}
        contentEditable
        suppressContentEditableWarning
        dir={dir}
        onInput={(e) => onChange((e.target as HTMLDivElement).innerHTML)}
        className="prose-content max-w-none px-3 py-2 focus:outline-none"
        style={{ minHeight }}
      />
    </div>
  );
}
