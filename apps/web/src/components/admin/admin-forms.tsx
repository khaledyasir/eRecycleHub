"use client";

import { Check, Copy, Mail, Paperclip, RotateCcw, Search, Trash2, X } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useEffect, useRef, useState } from "react";
import { toast } from "sonner";
import { CurrentFile } from "@/components/account/account-forms";
import { useAction } from "@/components/manage/manage-actions";
import { PasswordInput } from "@/components/public/auth-forms";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Checkbox, Field, FileInput, Input, Select } from "@/components/ui/form";
import { ConfirmDialog, Dialog } from "@/components/ui/overlay";
import { Switch } from "@/components/ui/controls";
import { useRouter } from "@/i18n/navigation";
import { api, call, sendForm, sendJson } from "@/lib/api/client";
import type { Schema } from "@/lib/api/types";
import { money } from "@/lib/format";
import { useForm, useValidators } from "@/lib/use-form";

type Country = { id: number; nameEn: string; nameAr: string };

/** On/off switch that POSTs {[field]: value} to `path` (activation, featured flag). */
export function FlagSwitch({ path, field, value, label, on, off }: { path: string; field: "active" | "featured"; value: boolean; label: string; on: string; off: string }) {
  const run = useAction();
  const [checked, setChecked] = useState(value);
  const [busy, setBusy] = useState(false);
  return (
    <Switch
      checked={checked}
      disabled={busy}
      label={label}
      onCheckedChange={async (next) => {
        setBusy(true);
        setChecked(next);
        try {
          await run(() => sendJson("POST", path, { [field]: next }), next ? on : off);
        } catch {
          setChecked(!next);
        } finally {
          setBusy(false);
        }
      }}
    />
  );
}

type AccountInitial = {
  fullName?: string; email?: string; phone?: string | null; countryId?: number; isActive?: boolean; companyName?: string | null; crNumber?: string | null;
  vatNumber?: string | null; bankName?: string | null; accountNumber?: string | null; cardNumber?: string | null;
  firstName?: string; lastName?: string | null; address?: string | null; secondMobile?: string | null;
  crDocument?: Schema<"FileRef"> | null; vatCertificate?: Schema<"FileRef"> | null; idDocument?: Schema<"FileRef"> | null;
};

/**
 * Create/edit form for customers (kind="user") and publishers (kind="publisher"). Wallet is never part of it:
 * balances only change through wallet transactions (an opening credit on create becomes an approved, ledgered credit).
 */
export function AccountForm({ kind, id, initial, countries }: { kind: "user" | "publisher"; id?: number; initial?: AccountInitial; countries: Country[] }) {
  const t = useTranslations("admin.users");
  const tp = useTranslations("admin.publishers");
  const tc = useTranslations("admin.common");
  const ts = useTranslations("account.settings");
  const locale = useLocale();
  const v = useValidators();
  const router = useRouter();
  const editing = id !== undefined;
  const f = useForm({
    fullName: initial?.fullName ?? "", email: initial?.email ?? "", phone: initial?.phone ?? "", countryId: String(initial?.countryId ?? countries[0]?.id ?? ""),
    password: "", isActive: initial?.isActive ?? true, companyName: initial?.companyName ?? "", crNumber: initial?.crNumber ?? "", vatNumber: initial?.vatNumber ?? "",
    bankName: initial?.bankName ?? "", accountNumber: initial?.accountNumber ?? "", cardNumber: initial?.cardNumber ?? "",
    firstName: initial?.firstName ?? "", lastName: initial?.lastName ?? "", address: initial?.address ?? "", secondMobile: initial?.secondMobile ?? "", openingCredit: "",
  });
  const [files, setFiles] = useState<Record<string, File | null>>({});
  const pick = (name: string) => (l: File[]) => setFiles((x) => ({ ...x, [name]: l[0] ?? null }));
  const images = "image/jpeg,image/png,image/webp";
  const docs = `${images},application/pdf`;

  return (
    <form
      noValidate
      className="space-y-8"
      onSubmit={f.submit(
        (x) => ({
          fullName: kind === "user" ? v.required(x.fullName) : undefined,
          firstName: kind === "publisher" ? v.required(x.firstName) : undefined,
          email: v.email(x.email),
          countryId: v.required(x.countryId),
          password: editing ? (x.password ? v.min(x.password, 8) : undefined) : v.min(x.password, 8),
          openingCredit: !editing && kind === "user" && x.openingCredit ? v.amount(x.openingCredit) : undefined,
        }),
        async (x) => {
          const account = {
            fullName: kind === "user" ? x.fullName : `${x.firstName} ${x.lastName}`.trim(), email: x.email, phone: x.phone || null, countryId: Number(x.countryId),
            password: x.password || null, isActive: x.isActive, companyName: x.companyName || null, crNumber: x.crNumber || null, vatNumber: x.vatNumber || null,
            bankName: x.bankName || null, accountNumber: x.accountNumber || null, cardNumber: x.cardNumber || null,
          };
          const path = kind === "user" ? "/api/admin/users" : "/api/admin/publishers";
          const data =
            kind === "user"
              ? editing ? account : { account, openingCredit: x.openingCredit ? Number(x.openingCredit) : 0 }
              : { firstName: x.firstName, lastName: x.lastName || null, address: x.address || null, secondMobile: x.secondMobile || null, account };
          if (editing) {
            await sendForm("PUT", `${path}/${id}`, data, files);
            toast.success(t("saved"));
            f.set("password", "");
            router.refresh();
          } else {
            const r = await sendForm<{ id: number }>("POST", path, data, files);
            toast.success(kind === "user" ? t("created") : tp("created"));
            router.push(kind === "user" ? `/admin/users/${r.id}` : `/admin/publishers/${r.id}`);
          }
        },
      )}
    >
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}
      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{tc("account")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          {kind === "user" ? (
            <Field label={tc("fullName")} error={f.errors.fullName} required className="sm:col-span-2">{(p) => <Input {...p} {...f.text("fullName")} />}</Field>
          ) : (
            <>
              <Field label={tp("firstName")} error={f.errors.firstName} required>{(p) => <Input {...p} {...f.text("firstName")} />}</Field>
              <Field label={tp("lastName")}>{(p) => <Input {...p} {...f.text("lastName")} />}</Field>
              <Field label={tp("address")} className="sm:col-span-2">{(p) => <Input {...p} {...f.text("address")} />}</Field>
            </>
          )}
          <Field label={tc("email")} error={f.errors.email} required>{(p) => <Input {...p} {...f.text("email")} type="email" autoComplete="off" />}</Field>
          <Field label={tc("phone")}>{(p) => <Input {...p} {...f.text("phone")} type="tel" dir="ltr" />}</Field>
          {kind === "publisher" && <Field label={tp("secondMobile")}>{(p) => <Input {...p} {...f.text("secondMobile")} type="tel" dir="ltr" />}</Field>}
          <Field label={tc("country")} error={f.errors.countryId} required>
            {(p) => (
              <Select {...p} {...f.text("countryId")}>
                {countries.map((c) => <option key={c.id} value={c.id}>{locale === "ar" ? c.nameAr : c.nameEn}</option>)}
              </Select>
            )}
          </Field>
          <Field label={editing ? t("passwordNew") : t("password")} hint={editing ? t("passwordKeep") : t("passwordHint")} error={f.errors.password} required={!editing}>
            {(p) => <PasswordInput {...p} {...f.text("password")} autoComplete="new-password" />}
          </Field>
          {!editing && kind === "user" && (
            <Field label={t("openingCredit")} hint={t("openingCreditHint")} error={f.errors.openingCredit}>
              {(p) => <Input {...p} {...f.text("openingCredit")} inputMode="decimal" dir="ltr" className="tabular" />}
            </Field>
          )}
        </div>
        <Checkbox label={tc("isActive")} checked={f.values.isActive} onChange={(e) => f.set("isActive", e.target.checked)} />
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{tc("company")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={ts("companyName")} className="sm:col-span-2">{(p) => <Input {...p} {...f.text("companyName")} />}</Field>
          <Field label={ts("crNumber")}>{(p) => <Input {...p} {...f.text("crNumber")} dir="ltr" />}</Field>
          <Field label={ts("vatNumber")}>{(p) => <Input {...p} {...f.text("vatNumber")} dir="ltr" />}</Field>
          <Field label={ts("bankName")}>{(p) => <Input {...p} {...f.text("bankName")} />}</Field>
          <Field label={ts("accountNumber")}>{(p) => <Input {...p} {...f.text("accountNumber")} dir="ltr" />}</Field>
          <Field label={ts("cardNumber")}>{(p) => <Input {...p} {...f.text("cardNumber")} dir="ltr" autoComplete="off" />}</Field>
        </div>
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{tc("documents")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          {kind === "user" ? (
            <Field label={tc("avatar")}>{(p) => <FileInput {...p} accept={images} onFiles={pick("avatar")} />}</Field>
          ) : (
            <>
              <Field label={tp("logo")}>{(p) => <FileInput {...p} accept={images} onFiles={pick("logo")} />}</Field>
              <Field label={tp("background")}>{(p) => <FileInput {...p} accept={images} onFiles={pick("background")} />}</Field>
            </>
          )}
          {(
            [
              ["crDocument", ts("crDocument"), initial?.crDocument],
              ["vatCertificate", ts("vatCertificate"), initial?.vatCertificate],
              ["idDocument", ts("idDocument"), initial?.idDocument],
            ] as const
          ).map(([name, label, current]) => (
            <Field key={name} label={label}>
              {(p) => (
                <div className="space-y-1">
                  <FileInput {...p} accept={docs} onFiles={pick(name)} />
                  <CurrentFile file={current} label={ts("current", { name: current?.name ?? "" })} />
                </div>
              )}
            </Field>
          ))}
        </div>
      </fieldset>
      <Button type="submit" loading={f.busy}>{editing ? tc("save") : tc("create")}</Button>
    </form>
  );
}

export function DeleteButton({ path, title, body, label, done, redirectTo }: { path: string; title: string; body?: string; label: string; done: string; redirectTo?: string }) {
  const run = useAction();
  const router = useRouter();
  return (
    <ConfirmDialog
      trigger={<Button variant="secondary"><Trash2 className="h-4 w-4 text-rust" aria-hidden /> {label}</Button>}
      title={title}
      body={body}
      confirmLabel={label}
      onConfirm={async () => {
        await run(() => sendJson("DELETE", path), done);
        if (redirectTo) router.push(redirectTo);
      }}
    />
  );
}

export function ApplicationActions({ app }: { app: Schema<"ApplicationRow"> }) {
  const t = useTranslations("admin.applications");
  const run = useAction();
  const [temp, setTemp] = useState<string | null>(null);
  return (
    <div className="flex flex-wrap gap-2">
      {app.status === "Pending" && (
        <>
          <ConfirmDialog
            tone="primary"
            trigger={<Button size="sm"><Check className="h-4 w-4" aria-hidden /> {t("approve")}</Button>}
            title={t("approveTitle", { name: app.name })}
            body={t("approveBody", { email: app.email })}
            confirmLabel={t("approve")}
            onConfirm={async () => {
              const r = (await run(() => sendJson("POST", `/api/admin/applications/${app.id}/approve`), t("approvedToast"))) as Schema<"ApprovalResult">;
              if (r?.temporaryPassword) setTemp(r.temporaryPassword);
            }}
          />
          <ConfirmDialog
            trigger={<Button size="sm" variant="secondary"><X className="h-4 w-4" aria-hidden /> {t("reject")}</Button>}
            title={t("rejectTitle", { name: app.name })}
            body={t("rejectBody")}
            confirmLabel={t("reject")}
            onConfirm={() => run(() => sendJson("POST", `/api/admin/applications/${app.id}/reject`), t("rejectedToast"))}
          />
        </>
      )}
      <ConfirmDialog
        trigger={<Button size="sm" variant="ghost" aria-label={t("delete")}><Trash2 className="h-4 w-4 text-rust" /></Button>}
        title={t("deleteTitle")}
        confirmLabel={t("delete")}
        onConfirm={() => run(() => sendJson("DELETE", `/api/admin/applications/${app.id}`), t("deletedToast"))}
      />
      <Dialog
        open={temp !== null}
        onOpenChange={(o) => !o && setTemp(null)}
        title={t("tempTitle")}
        description={t("tempBody")}
        footer={
          <Button
            onClick={async () => {
              await navigator.clipboard.writeText(temp ?? "");
              toast.success(t("copied"));
            }}
          >
            <Copy className="h-4 w-4" aria-hidden /> {t("copy")}
          </Button>
        }
      >
        <p className="tabular select-all rounded-md bg-sunken px-3 py-2 text-center font-mono text-lg" dir="ltr">{temp}</p>
      </Dialog>
    </div>
  );
}

export function TransactionActions({ tx }: { tx: Schema<"WalletTransactionDto"> }) {
  const t = useTranslations("admin.transactions");
  const locale = useLocale();
  const run = useAction();
  const fileRef = useRef<HTMLInputElement>(null);
  const args = { amount: money(tx.amount, locale), user: tx.userName ?? `#${tx.userId}` };
  const post = (action: string, done: string) => () => run(() => sendJson("POST", `/api/admin/transactions/${tx.id}/${action}`), done);
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {tx.status === "Pending" && (
        <>
          <ConfirmDialog tone="primary" trigger={<Button size="sm">{t("approve")}</Button>} title={t("approveTitle", args)} body={t("approveBody")} confirmLabel={t("approve")} onConfirm={post("approve", t("approved"))} />
          <ConfirmDialog trigger={<Button size="sm" variant="secondary">{t("reject")}</Button>} title={t("rejectTitle", args)} body={t("rejectBody")} confirmLabel={t("reject")} onConfirm={post("reject", t("rejected"))} />
        </>
      )}
      {tx.status === "Approved" && (
        <ConfirmDialog
          trigger={<Button size="sm" variant="secondary"><RotateCcw className="h-4 w-4" aria-hidden /> {t("reverse")}</Button>}
          title={t("reverseTitle", args)}
          body={t("reverseBody")}
          confirmLabel={t("reverse")}
          onConfirm={post("reverse", t("reversed"))}
        />
      )}
      {(tx.status === "Pending" || tx.status === "Rejected") && (
        <ConfirmDialog
          trigger={<Button size="sm" variant="ghost" aria-label={t("delete")}><Trash2 className="h-4 w-4 text-rust" /></Button>}
          title={t("deleteTitle")}
          body={t("deleteBody")}
          confirmLabel={t("delete")}
          onConfirm={() => run(() => sendJson("DELETE", `/api/admin/transactions/${tx.id}`), t("deleted"))}
        />
      )}
      <input
        ref={fileRef}
        type="file"
        accept="image/jpeg,image/png,image/webp,application/pdf"
        className="sr-only"
        aria-label={t("uploadProof")}
        onChange={async (e) => {
          const file = e.target.files?.[0];
          e.target.value = "";
          if (file) await run(() => sendForm("PUT", `/api/admin/transactions/${tx.id}/proof`, {}, { proof: file }), t("proofSaved")).catch(() => undefined);
        }}
      />
      <Button size="sm" variant="ghost" onClick={() => fileRef.current?.click()} aria-label={t("uploadProof")} title={t("uploadProof")}>
        <Paperclip className="h-4 w-4" />
      </Button>
      {tx.proofFileId && (
        <Button size="sm" variant="ghost" onClick={() => run(() => sendForm("PUT", `/api/admin/transactions/${tx.id}/proof`, {}), t("proofRemoved")).catch(() => undefined)} aria-label={t("removeProof")} title={t("removeProof")}>
          <X className="h-4 w-4" />
        </Button>
      )}
    </div>
  );
}

export function CreditForm({ defaultUser }: { defaultUser?: { id: number; name: string } }) {
  const t = useTranslations("admin.transactions");
  const tv = useTranslations("errors.validation");
  const v = useValidators();
  const router = useRouter();
  const [q, setQ] = useState("");
  const [options, setOptions] = useState<Schema<"UserRow">[]>([]);
  const [user, setUser] = useState(defaultUser ?? null);
  const [proof, setProof] = useState<File | null>(null);
  const f = useForm({ amount: "", auctionId: "", note: "" });

  const shown = q.trim().length < 2 ? [] : options;
  useEffect(() => {
    if (q.trim().length < 2) return;
    const handle = setTimeout(() => {
      call(() => api.GET("/api/admin/users/search", { params: { query: { q } } })).then(setOptions).catch(() => setOptions([]));
    }, 250);
    return () => clearTimeout(handle);
  }, [q]);

  return (
    <form
      noValidate
      className="max-w-xl space-y-4"
      onSubmit={f.submit(
        (x) => ({ amount: v.amount(x.amount), auctionId: x.auctionId && !/^\d+$/.test(x.auctionId) ? tv("positive") : undefined }),
        async (x) => {
          if (!user) {
            f.setFormError(t("accountPick"));
            return;
          }
          await sendForm("POST", "/api/admin/transactions", { userId: user.id, amount: Number(x.amount), auctionId: x.auctionId ? Number(x.auctionId) : null, note: x.note || null }, { proof });
          toast.success(t("created"));
          router.push("/admin/transactions");
        },
      )}
    >
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}
      <Field label={t("account")} required hint={user ? t("selected", { name: user.name }) : undefined}>
        {(p) => (
          <div className="space-y-2">
            <div className="relative">
              <Search className="pointer-events-none absolute start-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden />
              <Input {...p} value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("accountSearch")} className="ps-9" autoComplete="off" />
            </div>
            {shown.length > 0 && (
              <ul className="max-h-56 overflow-y-auto rounded-md border border-line bg-surface" role="listbox" aria-label={t("accountPick")}>
                {shown.map((o) => (
                  <li key={o.id}>
                    <button
                      type="button"
                      role="option"
                      aria-selected={user?.id === o.id}
                      onClick={() => {
                        setUser({ id: o.id, name: `${o.fullName} (${o.email})` });
                        setOptions([]);
                        setQ("");
                      }}
                      className="flex w-full items-center justify-between gap-3 px-3 py-2 text-start text-sm hover:bg-sunken"
                    >
                      <span>{o.fullName} <span className="text-muted">{o.email}</span></span>
                      <span className="tabular text-xs text-muted">{o.walletBalance.toFixed(2)}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </Field>
      <Field label={t("amount")} error={f.errors.amount} required>{(p) => <Input {...p} {...f.text("amount")} inputMode="decimal" dir="ltr" className="tabular max-w-48" />}</Field>
      <Field label={t("auctionId")} error={f.errors.auctionId}>{(p) => <Input {...p} {...f.text("auctionId")} inputMode="numeric" dir="ltr" className="max-w-48" />}</Field>
      <Field label={t("note")}>{(p) => <Input {...p} {...f.text("note")} maxLength={1000} />}</Field>
      <Field label={t("proofFile")}>{(p) => <FileInput {...p} accept="image/jpeg,image/png,image/webp,application/pdf" onFiles={(l) => setProof(l[0] ?? null)} />}</Field>
      <Button type="submit" loading={f.busy}>{t("submit")}</Button>
    </form>
  );
}

export function NotifyAllWinnersButton() {
  const t = useTranslations("admin.winners");
  const run = useAction();
  return (
    <ConfirmDialog
      tone="primary"
      trigger={<Button><Mail className="h-4 w-4" aria-hidden /> {t("notifyAll")}</Button>}
      title={t("notifyAllTitle")}
      body={t("notifyAllBody")}
      confirmLabel={t("notifyAll")}
      onConfirm={async () => {
        const r = (await run(() => sendJson("POST", "/api/admin/winners/notify"))) as Schema<"NotifyResult">;
        toast.success(t("sentResult", { sent: r.sent, failed: r.failed, already: r.alreadyNotified }));
      }}
    />
  );
}

