"use client";

import { BellOff, Check, ExternalLink, Trash2 } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { PasswordInput } from "@/components/public/auth-forms";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Field, FileInput, Input, Select } from "@/components/ui/form";
import { Link, useRouter } from "@/i18n/navigation";
import { api, call, sendForm, setCsrfToken } from "@/lib/api/client";
import type { Schema } from "@/lib/api/types";
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
        (x) => ({ current: v.required(x.current), next: v.min(x.next, 8), confirm: v.match(x.next, x.confirm) }),
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
      <Field label={t("new")} hint={t("newHint")} error={f.errors.next} required>
        {(p) => <PasswordInput {...p} {...f.text("next")} autoComplete="new-password" />}
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

export function ProfileForm({ profile, countries }: { profile: Schema<"ProfileDto">; countries: { id: number; nameEn: string; nameAr: string }[] }) {
  const t = useTranslations("account.settings");
  const locale = useLocale();
  const v = useValidators();
  const router = useRouter();
  const f = useForm({
    fullName: profile.fullName, phone: profile.phone ?? "", countryId: String(profile.countryId), companyName: profile.companyName ?? "",
    crNumber: profile.crNumber ?? "", vatNumber: profile.vatNumber ?? "", bankName: profile.bankName ?? "", accountNumber: profile.accountNumber ?? "", cardNumber: profile.cardNumber ?? "",
  });
  const [files, setFiles] = useState<Record<string, File | null>>({});
  const pickFile = (name: string) => (l: File[]) => setFiles((x) => ({ ...x, [name]: l[0] ?? null }));
  const docs = "image/jpeg,image/png,image/webp,application/pdf";
  return (
    <form
      noValidate
      className="space-y-8"
      onSubmit={f.submit(
        (x) => ({ fullName: v.required(x.fullName), countryId: v.required(x.countryId) }),
        async (x) => {
          await sendForm("PUT", "/api/account/profile", { ...x, countryId: Number(x.countryId) }, files);
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
          <Field label={t("phone")}>{(p) => <Input {...p} {...f.text("phone")} type="tel" dir="ltr" autoComplete="tel" />}</Field>
          <Field label={t("country")} error={f.errors.countryId} required>
            {(p) => (
              <Select {...p} {...f.text("countryId")}>
                {countries.map((c) => (
                  <option key={c.id} value={c.id}>
                    {locale === "ar" ? c.nameAr : c.nameEn}
                  </option>
                ))}
              </Select>
            )}
          </Field>
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
        <Field label={t("bankName")}>{(p) => <Input {...p} {...f.text("bankName")} />}</Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("accountNumber")}>{(p) => <Input {...p} {...f.text("accountNumber")} dir="ltr" />}</Field>
          <Field label={t("cardNumber")}>{(p) => <Input {...p} {...f.text("cardNumber")} dir="ltr" inputMode="numeric" autoComplete="off" />}</Field>
        </div>
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
