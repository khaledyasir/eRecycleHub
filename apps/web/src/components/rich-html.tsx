import { cn } from "@/lib/format";

/**
 * The only place the web app renders HTML. Every rich-text field is sanitised by the API (HtmlSanitizer whitelist,
 * no scripts, event handlers, iframes, styles or javascript: URLs) when it is written, so this renders stored output as is.
 */
export function RichHtml({ html, className, dir }: { html: string | null | undefined; className?: string; dir?: "ltr" | "rtl" }) {
  if (!html) return null;
  return <div className={cn("prose-content", className)} dir={dir} dangerouslySetInnerHTML={{ __html: html }} />;
}
