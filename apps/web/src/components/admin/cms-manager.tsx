"use client";

import { Pencil, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { useAction } from "@/components/manage/manage-actions";
import { Button } from "@/components/ui/button";
import { Table } from "@/components/ui/controls";
import { Badge, Alert, EmptyState } from "@/components/ui/display";
import { Checkbox, Field, FileInput, Input, Select, Textarea } from "@/components/ui/form";
import { ConfirmDialog, Dialog } from "@/components/ui/overlay";
import { RichTextEditor } from "@/components/ui/rich-text";
import { sendForm, sendJson } from "@/lib/api/client";
import { fileUrl } from "@/lib/format";
import { useErrorMessage } from "@/lib/use-error-message";

export type CmsField = {
  name: string;
  label: string;
  kind: "text" | "area" | "rich" | "number" | "bool" | "select" | "file" | "url";
  options?: { value: string | number; label: string }[];
  /** Arabic text fields are entered right-to-left. */
  rtl?: boolean;
  required?: boolean;
  hint?: string;
  accept?: string;
  /** For file fields: the row property holding the stored file id (shown as the current file). */
  current?: string;
};

export type CmsColumn = { key: string; label: string; kind?: "text" | "bool" | "file" | "html" | "option"; options?: Record<string, string> };

export type CmsLabels = {
  add: string; edit: string; save: string; create: string; cancel: string; delete: string; deleteTitle: string; deleteBody?: string;
  saved: string; deleted: string; empty: string; yes: string; no: string; actions: string; required: string; currentFile: string;
};

type Row = Record<string, unknown> & { id: number };
type Values = Record<string, string | boolean>;

const IMAGE = "image/png,image/jpeg,image/webp,image/gif";

/**
 * Generic list + create/edit dialog for the simple content tables (categories, countries, slides, FAQ, …).
 * `multipart` endpoints take JSON in "data" plus named file parts; the others take plain JSON.
 * The API validates and sanitises everything; the client only checks required fields.
 */
export function CmsManager({ path, fields, columns, rows, labels, multipart = false, defaults = {}, single = false, titleKeys }: {
  path: string;
  fields: CmsField[];
  columns: CmsColumn[];
  rows: Row[];
  labels: CmsLabels;
  multipart?: boolean;
  defaults?: Values;
  /** Only one record makes sense (e.g. the About page): hide "add" once it exists. */
  single?: boolean;
  /** Row properties (first non-empty wins) naming a record in dialog titles and delete confirmation. */
  titleKeys: string[];
}) {
  const [editing, setEditing] = useState<Row | "new" | null>(null);
  const run = useAction();
  const title = (r: Row) => titleKeys.map((k) => String(r[k] ?? "").trim()).find(Boolean) ?? `#${r.id}`;
  return (
    <div>
      {(!single || rows.length === 0) && (
        <div className="mb-4">
          <Button onClick={() => setEditing("new")}><Plus className="h-4 w-4" aria-hidden /> {labels.add}</Button>
        </div>
      )}
      {rows.length === 0 ? (
        <EmptyState title={labels.empty} />
      ) : (
        <Table>
          <thead>
            <tr>
              {columns.map((c) => <th key={c.key}>{c.label}</th>)}
              <th><span className="sr-only">{labels.actions}</span></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                {columns.map((c) => (
                  <td key={c.key} className={c.kind === "html" ? "max-w-xs" : undefined}>
                    <Cell col={c} row={r} labels={labels} />
                  </td>
                ))}
                <td>
                  <div className="flex items-center justify-end gap-1">
                    <Button variant="ghost" size="sm" onClick={() => setEditing(r)} aria-label={`${labels.edit}: ${title(r)}`}>
                      <Pencil className="h-4 w-4" aria-hidden />
                    </Button>
                    <ConfirmDialog
                      trigger={<Button variant="ghost" size="sm" aria-label={`${labels.delete}: ${title(r)}`}><Trash2 className="h-4 w-4 text-rust" aria-hidden /></Button>}
                      title={labels.deleteTitle}
                      body={labels.deleteBody ? `${title(r)} — ${labels.deleteBody}` : title(r)}
                      confirmLabel={labels.delete}
                      onConfirm={() => run(() => sendJson("DELETE", `${path}/${r.id}`), labels.deleted)}
                    />
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      {editing && (
        <EditDialog
          key={editing === "new" ? "new" : editing.id}
          row={editing === "new" ? null : editing}
          path={path}
          fields={fields}
          defaults={defaults}
          labels={labels}
          multipart={multipart}
          heading={editing === "new" ? labels.add : `${labels.edit}: ${title(editing)}`}
          onClose={() => setEditing(null)}
        />
      )}
    </div>
  );
}

function Cell({ col, row, labels }: { col: CmsColumn; row: Row; labels: CmsLabels }) {
  const v = row[col.key];
  switch (col.kind) {
    case "bool":
      return <Badge tone={v ? "ok" : "neutral"}>{v ? labels.yes : labels.no}</Badge>;
    case "file":
      // eslint-disable-next-line @next/next/no-img-element -- admin thumbnails of uploaded files; sizes are unknown
      return v ? <img src={fileUrl(String(v))} alt="" className="h-10 w-16 rounded border border-line object-cover" /> : <span className="text-muted">—</span>;
    case "html":
      return <span className="line-clamp-2 text-sm text-ink-2">{String(v ?? "").replace(/<[^>]+>/g, " ").trim() || "—"}</span>;
    case "option":
      return <>{col.options?.[String(v)] ?? String(v ?? "—")}</>;
    default:
      return <>{v === null || v === undefined || v === "" ? "—" : String(v)}</>;
  }
}

function EditDialog({ row, path, fields, defaults, labels, multipart, heading, onClose }: {
  row: Row | null; path: string; fields: CmsField[]; defaults: Values; labels: CmsLabels; multipart: boolean; heading: string; onClose: () => void;
}) {
  const message = useErrorMessage();
  const run = useAction();
  const initial: Values = {};
  for (const f of fields) {
    if (f.kind === "file") continue;
    const v = row ? row[f.name] : defaults[f.name];
    initial[f.name] = f.kind === "bool" ? Boolean(v ?? true) : v === null || v === undefined ? (defaults[f.name] as string | undefined) ?? "" : String(v);
  }
  const [values, setValues] = useState<Values>(initial);
  const [files, setFiles] = useState<Record<string, File | null>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const set = (k: string, v: string | boolean) => {
    setValues((s) => ({ ...s, [k]: v }));
    setErrors((e) => (e[k] ? { ...e, [k]: "" } : e));
  };

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    const errs: Record<string, string> = {};
    for (const f of fields) if (f.required && f.kind !== "file" && f.kind !== "bool" && !String(values[f.name] ?? "").trim()) errs[f.name] = labels.required;
    setErrors(errs);
    const first = Object.keys(errs)[0];
    if (first) {
      (document.querySelector(`[name="${first}"]`) as HTMLElement | null)?.focus();
      return;
    }
    const data = Object.fromEntries(
      fields.filter((f) => f.kind !== "file").map((f) => [f.name, f.kind === "number" || (f.kind === "select" && typeof f.options?.[0]?.value === "number") ? Number(values[f.name] || 0) : values[f.name]]),
    );
    setBusy(true);
    setFormError(null);
    try {
      const url = row ? `${path}/${row.id}` : path;
      await run(() => (multipart ? sendForm(row ? "PUT" : "POST", url, data, files) : sendJson(row ? "PUT" : "POST", url, data)), labels.saved);
      onClose();
    } catch (err) {
      setFormError(message(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(o) => !o && !busy && onClose()}
      title={heading}
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>{labels.cancel}</Button>
          <Button type="submit" form="cms-form" loading={busy}>{row ? labels.save : labels.create}</Button>
        </>
      }
    >
      <form id="cms-form" onSubmit={submit} noValidate className="grid gap-4 sm:grid-cols-2">
        {formError && <div className="sm:col-span-2"><Alert tone="danger">{formError}</Alert></div>}
        {fields.map((f) => {
          const wide = f.kind === "rich" || f.kind === "area" || f.kind === "file" || f.kind === "bool";
          const cls = wide ? "sm:col-span-2" : undefined;
          if (f.kind === "bool") {
            return <div key={f.name} className={cls}><Checkbox name={f.name} label={f.label} checked={Boolean(values[f.name])} onChange={(e) => set(f.name, e.target.checked)} /></div>;
          }
          if (f.kind === "file") {
            const currentId = row && f.current ? (row[f.current] as string | null) : null;
            return (
              <Field key={f.name} label={f.label} hint={f.hint} className={cls}>
                {(p) => (
                  <>
                    <FileInput {...p} accept={f.accept ?? IMAGE} onFiles={(l) => setFiles((s) => ({ ...s, [f.name]: l[0] ?? null }))} current={currentId ? labels.currentFile : null} />
                    {currentId && !files[f.name] && (
                      // eslint-disable-next-line @next/next/no-img-element -- preview of an uploaded file
                      <img src={fileUrl(currentId)} alt="" className="mt-1 h-16 w-auto max-w-40 rounded border border-line object-contain" />
                    )}
                  </>
                )}
              </Field>
            );
          }
          return (
            <Field key={f.name} label={f.label} hint={f.hint} error={errors[f.name]} required={f.required} className={cls}>
              {(p) => {
                const aria = { id: p.id, "aria-invalid": p["aria-invalid"], "aria-describedby": p["aria-describedby"] };
                const common = { name: f.name, value: String(values[f.name] ?? ""), dir: f.rtl ? "rtl" : f.kind === "url" ? "ltr" : undefined };
                if (f.kind === "rich")
                  return <RichTextEditor id={p.id} value={String(values[f.name] ?? "")} onChange={(h) => set(f.name, h)} dir={f.rtl ? "rtl" : "ltr"} aria-describedby={p["aria-describedby"]} aria-invalid={p["aria-invalid"]} />;
                if (f.kind === "area") return <Textarea rows={4} {...aria} {...common} onChange={(e) => set(f.name, e.target.value)} />;
                if (f.kind === "select")
                  return (
                    <Select {...aria} name={f.name} value={String(values[f.name] ?? "")} onChange={(e) => set(f.name, e.target.value)}>
                      {!f.required && <option value="" />}
                      {f.options?.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
                    </Select>
                  );
                return <Input {...aria} {...common} type={f.kind === "number" ? "number" : "text"} inputMode={f.kind === "number" ? "numeric" : undefined} onChange={(e) => set(f.name, e.target.value)} />;
              }}
            </Field>
          );
        })}
      </form>
    </Dialog>
  );
}
