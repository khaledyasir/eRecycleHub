"use client";

import { BellOff, Check, ExternalLink, Trash2 } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { PasswordChecklist, PasswordInput } from "@/components/public/auth-forms";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Field, FileInput, Input, PhoneInput } from "@/components/ui/form";
import { ConfirmDialog } from "@/components/ui/overlay";
import { Link, useRouter } from "@/i18n/navigation";
import { api, call, sendForm, setCsrfToken } from "@/lib/api/client";
import { ApiError, type Schema } from "@/lib/api/types";
import { fileUrl, money } from "@/lib/format";
import { useErrorMessage } from "@/lib/use-error-message";
import { useForm, useValidators } from "@/lib/use-form";

export function TopUpForm({ max }: { max: number }) {
  const t = useTranslations("account.wallet");
  const locale = useLocale();
  const v = useValidators();
  const router = useRouter();
  const [proof, setProof] = useState<File | null>(null);
  const [fileKey, setFileKey] = useState(0);
  // One idempotency key per request being entered: a retry after a network error reuses it, so no duplicate request is created.
  const [key, setKey] = useState(() => crypto.randomUUID());
  const f = useForm({ amount: "" });
  return (
    <form
      noValidate
      className="space-y-4"
      onSubmit={f.submit(
        (x) => ({ amount: v.amount(x.amount) ?? (Number(x.amount) > max ? t("amountHint", { max: money(max, locale) }) : undefined) }),
        async (x) => {
          await sendForm("POST", "/api/account/wallet/top-ups", { amount: Number(x.amount), idempotencyKey: key }, { proof });
          setKey(crypto.randomUUID());
          f.set("amount", "");
          setProof(null);
          setFileKey((k) => k + 1);
          toast.success(t("submitted"));
          router.refresh();
        },
      )}
    >
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}
      <Field label={t("amount")} hint={t("amountHint", { max: money(max, locale) })} error={f.errors.amount} required>
        {(p) => <Input {...p} {...f.text("amount")} inputMode="decimal" dir="ltr" className="tabular max-w-48" />}
      </Field>
      <Field label={t("proof")} hint={t("proofHint")}>
        {(p) => <FileInput key={fileKey} {...p} accept="image/jpeg,image/png,image/webp,application/pdf" onFiles={(l) => setProof(l[0] ?? null)} />}
      </Field>
      <Button type="submit" loading={f.busy}>
        {t("submit")}
      </Button>
    </form>
  );
}

/** Withdrawal request. The API checks the amount against the available balance under a lock; `max` only helps the user enter it. */
export function WithdrawForm({ max, bank }: { max: number; bank: { name: string; account: string } | null }) {
  const t = useTranslations("account.wallet");
  const locale = useLocale();
  const v = useValidators();
  const router = useRouter();
  const [key, setKey] = useState(() => crypto.randomUUID());
  const f = useForm({ amount: "" });
  if (!bank)
    return (
      <Alert tone="info">
        <p>{t("bankMissing")}</p>
        <Link href="/account/settings" className="mt-2 inline-block font-semibold underline">{t("bankMissingLink")}</Link>
      </Alert>
    );
  return (
    <form
      noValidate
      className="space-y-4"
      onSubmit={f.submit(
        (x) => ({ amount: v.amount(x.amount) ?? (Number(x.amount) > max ? t("withdrawAmountHint", { max: money(max, locale) }) : undefined) }),
        async (x) => {
          await call(() => api.POST("/api/account/wallet/withdrawals", { body: { amount: Number(x.amount), idempotencyKey: key } }));
          setKey(crypto.randomUUID());
          f.set("amount", "");
          toast.success(t("withdrawSubmitted"));
          router.refresh();
        },
      )}
    >
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}
      <Field label={t("withdrawAmount")} hint={t("withdrawAmountHint", { max: money(max, locale) })} error={f.errors.amount} required>
        {(p) => (
          <div className="flex items-center gap-2">
            <Input {...p} {...f.text("amount")} inputMode="decimal" dir="ltr" className="tabular max-w-48" />
            <Button type="button" variant="secondary" size="sm" onClick={() => f.set("amount", String(max))} disabled={max <= 0}>{t("max")}</Button>
          </div>
        )}
      </Field>
      <p className="text-xs text-muted">{t("withdrawTo", { bank: bank.name, account: bank.account })}</p>
      <Button type="submit" variant="secondary" loading={f.busy} disabled={max <= 0}>
        {t("withdrawSubmit")}
      </Button>
    </form>
  );
}

export function PasswordForm({ ns = "account.password" }: { ns?: string }) {
  const t = useTranslations(ns);
  const v = useValidators();
  const f = useForm({ current: "", next: "", confirm: "" });
  const [done, setDone] = useState(false);
  return (
    <form
      noValidate
      className="max-w-md space-y-4"
      onSubmit={f.submit(
        (x) => ({ current: v.required(x.current), next: v.strongPassword(x.next), confirm: v.match(x.next, x.confirm) }),
        async (x) => {
          const me = await call(() => api.POST("/api/auth/change-password", { body: { currentPassword: x.current, newPassword: x.next } }));
          setCsrfToken(me.csrfToken);
          f.set("current", "");
          f.set("next", "");
          f.set("confirm", "");
          setDone(true);
          toast.success(t("done"));
        },
      )}
    >
      {done && <Alert tone="ok">{t("done")}</Alert>}
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}
      <Field label={t("current")} error={f.errors.current} required>
        {(p) => <PasswordInput {...p} {...f.text("current")} autoComplete="current-password" />}
      </Field>
      <Field label={t("new")} error={f.errors.next} required>
        {(p) => (
          <>
            <PasswordInput {...p} {...f.text("next")} autoComplete="new-password" />
            <PasswordChecklist value={f.values.next} />
          </>
        )}
      </Field>
      <Field label={t("confirm")} error={f.errors.confirm} required>
        {(p) => <PasswordInput {...p} {...f.text("confirm")} autoComplete="new-password" />}
      </Field>
      <Button type="submit" loading={f.busy}>
        {t("submit")}
      </Button>
    </form>
  );
}

/** Existing private document: a link to open it (served only to the owner and admins). */
export function CurrentFile({ file, label }: { file: Schema<"FileRef"> | null | undefined; label: string }) {
  if (!file) return null;
  return (
    <a href={fileUrl(file.id)} target="_blank" rel="noopener" className="inline-flex items-center gap-1 text-xs text-patina hover:underline">
      <ExternalLink className="h-3 w-3" aria-hidden /> {label}
    </a>
  );
}

export function ProfileForm({ profile }: { profile: Schema<"ProfileDto"> }) {
  const t = useTranslations("account.settings");
  const v = useValidators();
  const router = useRouter();
  const f = useForm(
    {
      fullName: profile.fullName, phone: profile.phone ?? "", companyName: profile.companyName ?? "",
      crNumber: profile.crNumber ?? "", vatNumber: profile.vatNumber ?? "", bankName: profile.bankName ?? "", accountHolderName: profile.accountHolderName ?? "", accountNumber: profile.accountNumber ?? "",
    },
    (x) => ({ phone: v.phoneOptional(x.phone) }),
  );
  const [files, setFiles] = useState<Record<string, File | null>>({});
  const pickFile = (name: string) => (l: File[]) => setFiles((x) => ({ ...x, [name]: l[0] ?? null }));
  const docs = "image/jpeg,image/png,image/webp,application/pdf";
  return (
    <form
      noValidate
      className="space-y-8"
      onSubmit={f.submit(
        (x) => ({ fullName: v.required(x.fullName), phone: v.phoneOptional(x.phone) }),
        async (x) => {
          await sendForm("PUT", "/api/account/profile", x, files);
          setFiles({});
          toast.success(t("saved"));
          router.refresh();
        },
      )}
    >
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}
      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{t("personal")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("fullName")} error={f.errors.fullName} required>
            {(p) => <Input {...p} {...f.text("fullName")} autoComplete="name" />}
          </Field>
          <Field label={t("email")} hint={t("emailHint")}>
            {(p) => <Input {...p} value={profile.email} readOnly disabled />}
          </Field>
          <Field label={t("phone")} error={f.errors.phone}>{(p) => <PhoneInput {...p} {...f.text("phone")} />}</Field>
        </div>
        <Field label={t("avatar")}>
          {(p) => (
            <div className="flex items-center gap-3">
              {profile.avatarFileId && (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={fileUrl(profile.avatarFileId)} alt="" className="h-12 w-12 rounded-md border border-line object-cover" />
              )}
              <FileInput {...p} accept="image/jpeg,image/png,image/webp" onFiles={pickFile("avatar")} />
            </div>
          )}
        </Field>
      </fieldset>
      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{t("company")}</legend>
        <Field label={t("companyName")}>{(p) => <Input {...p} {...f.text("companyName")} autoComplete="organization" />}</Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("crNumber")}>{(p) => <Input {...p} {...f.text("crNumber")} dir="ltr" />}</Field>
          <Field label={t("vatNumber")}>{(p) => <Input {...p} {...f.text("vatNumber")} dir="ltr" />}</Field>
        </div>
      </fieldset>
      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{t("bank")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("bankName")}>{(p) => <Input {...p} {...f.text("bankName")} />}</Field>
          <Field label={t("accountHolder")}>{(p) => <Input {...p} {...f.text("accountHolderName")} />}</Field>
        </div>
        <Field label={t("accountNumber")}>{(p) => <Input {...p} {...f.text("accountNumber")} dir="ltr" />}</Field>
      </fieldset>
      <fieldset className="space-y-4">
        <legend className="text-lg font-semibold">{t("documents")}</legend>
        <p className="text-sm text-muted">{t("documentsHint")}</p>
        {(
          [
            ["crDocument", t("crDocument"), profile.crDocument],
            ["vatCertificate", t("vatCertificate"), profile.vatCertificate],
            ["idDocument", t("idDocument"), profile.idDocument],
          ] as const
        ).map(([name, label, current]) => (
          <Field key={name} label={label}>
            {(p) => (
              <div className="space-y-1">
                <FileInput {...p} accept={docs} onFiles={pickFile(name)} />
                <CurrentFile file={current} label={t("current", { name: current?.name ?? "" })} />
              </div>
            )}
          </Field>
        ))}
      </fieldset>
      <Button type="submit" loading={f.busy}>
        {t("save")}
      </Button>
    </form>
  );
}

/** Danger zone on the settings page: the app's "delete account". Refused by the API while money is unsettled. */
export function DeleteAccount() {
  const t = useTranslations("account.settings.delete");
  const router = useRouter();
  const message = useErrorMessage();
  const [blocked, setBlocked] = useState(false);
  return (
    <section className="mt-8 max-w-3xl space-y-4 rounded-lg border border-rust/30 bg-surface p-5 sm:p-7">
      <div>
        <h2 className="text-lg font-semibold">{t("title")}</h2>
        <p className="mt-1 text-sm text-muted">{t("description")}</p>
      </div>
      {blocked && (
        <Alert tone="danger" title={t("blockedTitle")}>
          <p>{t("blockedHelp")}</p>
          <Link href="/account/wallet" className="mt-2 inline-block font-semibold underline">{t("openWallet")}</Link>
        </Alert>
      )}
      <ConfirmDialog
        trigger={
          <Button variant="danger">
            <Trash2 className="h-4 w-4" aria-hidden /> {t("title")}
          </Button>
        }
        title={t("confirmTitle")}
        body={t("confirmBody")}
        confirmLabel={t("title")}
        onConfirm={async () => {
          setBlocked(false);
          try {
            const me = await call(() => api.DELETE("/api/account/profile"));
            setCsrfToken(me.csrfToken);
            toast.success(t("done"));
            router.push("/");
            router.refresh();
          } catch (e) {
            // Blocked: close the dialog and explain what to do first. Anything else: keep it open for a retry.
            if (e instanceof ApiError && e.code === "account.delete_blocked") return setBlocked(true);
            toast.error(message(e));
            throw e;
          }
        }}
      />
    </section>
  );
}

export function StopWatchButton({ auctionId }: { auctionId: number }) {
  const t = useTranslations("account.lists");
  const router = useRouter();
  const message = useErrorMessage();
  const [busy, setBusy] = useState(false);
  return (
    <Button
      variant="secondary"
      size="sm"
      loading={busy}
      onClick={async () => {
        setBusy(true);
        try {
          await call(() => api.DELETE("/api/auctions/{id}/watch", { params: { path: { id: auctionId } } }));
          toast.success(t("stopped"));
          router.refresh();
        } catch (e) {
          toast.error(message(e));
        } finally {
          setBusy(false);
        }
      }}
    >
      <BellOff className="h-4 w-4" aria-hidden /> {t("stop")}
    </Button>
  );
}

export function NotificationActions({ id, isRead, url }: { id: number; isRead: boolean; url: string | null }) {
  const t = useTranslations("account.notifications");
  const router = useRouter();
  const message = useErrorMessage();
  const [busy, setBusy] = useState(false);
  const run = async (fn: () => Promise<unknown>, done?: string) => {
    setBusy(true);
    try {
      await fn();
      if (done) toast.success(done);
      router.refresh();
    } catch (e) {
      toast.error(message(e));
    } finally {
      setBusy(false);
    }
  };
  const path = { params: { path: { id } } };
  return (
    <div className="flex flex-wrap gap-2">
      {url?.startsWith("/") && (
        <Button asChild size="sm" variant="secondary">
          <Link href={url} onClick={() => !isRead && void call(() => api.POST("/api/account/notifications/{id}/read", path))}>
            {t("open")}
          </Link>
        </Button>
      )}
      {!isRead && (
        <Button size="sm" variant="ghost" disabled={busy} onClick={() => run(() => call(() => api.POST("/api/account/notifications/{id}/read", path)))}>
          <Check className="h-4 w-4" aria-hidden /> {t("markRead")}
        </Button>
      )}
      <Button size="sm" variant="ghost" disabled={busy} onClick={() => run(() => call(() => api.DELETE("/api/account/notifications/{id}", path)), t("deleted"))} aria-label={t("delete")}>
        <Trash2 className="h-4 w-4" aria-hidden />
      </Button>
    </div>
  );
}

export function MarkAllReadButton() {
  const t = useTranslations("account.notifications");
  const router = useRouter();
  const message = useErrorMessage();
  const [busy, setBusy] = useState(false);
  return (
    <Button
      variant="secondary"
      size="sm"
      loading={busy}
      onClick={async () => {
        setBusy(true);
        try {
          await call(() => api.POST("/api/account/notifications/read-all"));
          toast.success(t("markedAll"));
          router.refresh();
        } catch (e) {
          toast.error(message(e));
        } finally {
          setBusy(false);
        }
      }}
    >
      {t("markAll")}
    </Button>
  );
}
