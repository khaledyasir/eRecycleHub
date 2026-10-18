"use client";

import { Eye, EyeOff } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Field, FileInput, Input, Select } from "@/components/ui/form";
import { Link, useRouter } from "@/i18n/navigation";
import { api, call, sendForm, setCsrfToken } from "@/lib/api/client";
import type { Schema, UserRole } from "@/lib/api/types";
import { useForm, useValidators } from "@/lib/use-form";

type Country = { id: number; nameEn: string; nameAr: string };
type Me = Schema<"MeResponse">;

export const homeFor = (role: UserRole) => (role === "Admin" ? "/admin" : "/account");
/** Only same-site relative paths are followed after sign-in (no open redirect). */
export const safeNext = (next: string | null | undefined) => (next && next.startsWith("/") && !next.startsWith("//") && !next.startsWith("/\\") ? next : null);

export function AuthCard({ title, description, children, footer }: { title: string; description?: string; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="container-page flex justify-center py-10 sm:py-16">
      <div className="w-full max-w-lg">
        <h1 className="text-2xl font-semibold sm:text-3xl">{title}</h1>
        {description && <p className="mt-2 text-muted">{description}</p>}
        <div className="mt-6 rounded-xl border border-line bg-surface p-5 sm:p-7">{children}</div>
        {footer && <div className="mt-5 space-y-2 text-sm text-ink-2">{footer}</div>}
      </div>
    </div>
  );
}

export function PasswordInput(props: React.ComponentProps<typeof Input>) {
  const t = useTranslations("auth.login");
  const [shown, setShown] = useState(false);
  return (
    <div className="relative">
      <Input {...props} type={shown ? "text" : "password"} className="pe-11" />
      <button type="button" onClick={() => setShown((s) => !s)} className="absolute end-1 top-1 inline-flex h-8 w-8 items-center justify-center rounded text-muted hover:bg-sunken" aria-label={shown ? t("hide") : t("show")} aria-pressed={shown}>
        {shown ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
      </button>
    </div>
  );
}

function FormError({ error }: { error: string | null }) {
  return error ? <Alert tone="danger">{error}</Alert> : null;
}

export function LoginForm({ next, adminOnly = false }: { next?: string; adminOnly?: boolean }) {
  const t = useTranslations("auth.login");
  const v = useValidators();
  const router = useRouter();
  const f = useForm({ email: "", password: "" });
  return (
    <form
      noValidate
      className="space-y-4"
      onSubmit={f.submit(
        (x) => ({ email: v.email(x.email), password: v.required(x.password) }),
        async (x) => {
          const me = await call<Me>(() => api.POST("/api/auth/login", { body: { email: x.email, password: x.password, adminOnly } }));
          setCsrfToken(me.csrfToken);
          router.push(safeNext(next) ?? homeFor(me.user!.role));
          router.refresh();
        },
      )}
    >
      <FormError error={f.formError} />
      <Field label={t("email")} error={f.errors.email} required>
        {(p) => <Input {...p} {...f.text("email")} type="email" autoComplete="email" />}
      </Field>
      <Field label={t("password")} error={f.errors.password} required>
        {(p) => <PasswordInput {...p} {...f.text("password")} autoComplete="current-password" />}
      </Field>
      <div className="flex items-center justify-between gap-3">
        <Link href="/forgot-password" className="text-sm text-patina hover:underline">
          {t("forgot")}
        </Link>
        <Button type="submit" loading={f.busy}>
          {t("submit")}
        </Button>
      </div>
    </form>
  );
}

export function RegisterForm({ countries, defaultCountry }: { countries: Country[]; defaultCountry?: number }) {
  const t = useTranslations("auth.register");
  const v = useValidators();
  const locale = useLocale();
  const router = useRouter();
  const f = useForm({
    fullName: "", email: "", phone: "", countryId: String(defaultCountry ?? countries[0]?.id ?? ""), password: "", confirm: "",
    companyName: "", crNumber: "", vatNumber: "", bankName: "", accountNumber: "", cardNumber: "",
  });
  const [files, setFiles] = useState<Record<string, File | null>>({});
  const file = (name: string) => (list: File[]) => setFiles((x) => ({ ...x, [name]: list[0] ?? null }));
  const docs = "image/jpeg,image/png,image/webp,application/pdf";
  return (
    <form
      noValidate
      className="space-y-8"
      onSubmit={f.submit(
        (x) => ({
          fullName: v.required(x.fullName), email: v.email(x.email), phone: v.required(x.phone), countryId: v.required(x.countryId),
          password: v.min(x.password, 8), confirm: v.match(x.password, x.confirm),
        }),
        async (x) => {
          const { confirm: _c, countryId, ...rest } = x;
          void _c;
          const me = await sendForm<Me>("POST", "/api/auth/register", { ...rest, countryId: Number(countryId) }, {
            avatar: files.avatar, crDocument: files.crDocument, vatCertificate: files.vatCertificate, idDocument: files.idDocument,
          });
          setCsrfToken(me.csrfToken);
          router.push("/account");
          router.refresh();
        },
      )}
    >
      <FormError error={f.formError} />
      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{t("account")}</legend>
        <Field label={t("fullName")} error={f.errors.fullName} required>
          {(p) => <Input {...p} {...f.text("fullName")} autoComplete="name" />}
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("email")} error={f.errors.email} required>
            {(p) => <Input {...p} {...f.text("email")} type="email" autoComplete="email" />}
          </Field>
          <Field label={t("phone")} error={f.errors.phone} required>
            {(p) => <Input {...p} {...f.text("phone")} type="tel" autoComplete="tel" dir="ltr" />}
          </Field>
        </div>
        <Field label={t("country")} error={f.errors.countryId} required>
          {(p) => (
            <Select {...p} {...f.text("countryId")}>
              <option value="">{t("chooseCountry")}</option>
              {countries.map((c) => (
                <option key={c.id} value={c.id}>
                  {locale === "ar" ? c.nameAr : c.nameEn}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("password")} hint={t("passwordHint")} error={f.errors.password} required>
            {(p) => <PasswordInput {...p} {...f.text("password")} autoComplete="new-password" />}
          </Field>
          <Field label={t("confirm")} error={f.errors.confirm} required>
            {(p) => <PasswordInput {...p} {...f.text("confirm")} autoComplete="new-password" />}
          </Field>
        </div>
        <Field label={t("avatar")}>{(p) => <FileInput {...p} accept="image/jpeg,image/png,image/webp" onFiles={file("avatar")} />}</Field>
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="text-lg font-semibold">{t("company")}</legend>
        <p className="text-sm text-muted">{t("companyHint")}</p>
        <Field label={t("companyName")}>{(p) => <Input {...p} {...f.text("companyName")} autoComplete="organization" />}</Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("crNumber")}>{(p) => <Input {...p} {...f.text("crNumber")} dir="ltr" />}</Field>
          <Field label={t("vatNumber")}>{(p) => <Input {...p} {...f.text("vatNumber")} dir="ltr" />}</Field>
        </div>
        <Field label={t("crDocument")} hint={t("documentsHint")}>{(p) => <FileInput {...p} accept={docs} onFiles={file("crDocument")} />}</Field>
        <Field label={t("vatCertificate")}>{(p) => <FileInput {...p} accept={docs} onFiles={file("vatCertificate")} />}</Field>
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="text-lg font-semibold">{t("bank")}</legend>
        <Field label={t("bankName")}>{(p) => <Input {...p} {...f.text("bankName")} />}</Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("accountNumber")}>{(p) => <Input {...p} {...f.text("accountNumber")} dir="ltr" />}</Field>
          <Field label={t("cardNumber")}>{(p) => <Input {...p} {...f.text("cardNumber")} dir="ltr" inputMode="numeric" autoComplete="off" />}</Field>
        </div>
        <Field label={t("idDocument")}>{(p) => <FileInput {...p} accept={docs} onFiles={file("idDocument")} />}</Field>
      </fieldset>

      <Button type="submit" size="lg" className="w-full" loading={f.busy}>
        {t("submit")}
      </Button>
    </form>
  );
}

export function ForgotForm() {
  const t = useTranslations("auth.forgot");
  const v = useValidators();
  const [sent, setSent] = useState(false);
  const f = useForm({ email: "" });
  if (sent)
    return (
      <div className="space-y-4">
        <Alert tone="ok">{t("sent")}</Alert>
        <Link href="/login" className="text-sm font-medium text-patina hover:underline">
          {t("back")}
        </Link>
      </div>
    );
  return (
    <form
      noValidate
      className="space-y-4"
      onSubmit={f.submit(
        (x) => ({ email: v.email(x.email) }),
        async (x) => {
          await call(() => api.POST("/api/auth/forgot-password", { body: { email: x.email } }));
          setSent(true);
        },
      )}
    >
      <FormError error={f.formError} />
      <Field label={t("email")} error={f.errors.email} required>
        {(p) => <Input {...p} {...f.text("email")} type="email" autoComplete="email" />}
      </Field>
      <Button type="submit" className="w-full" loading={f.busy}>
        {t("submit")}
      </Button>
    </form>
  );
}

export function ResetForm({ token }: { token: string }) {
  const t = useTranslations("auth.reset");
  const v = useValidators();
  const [done, setDone] = useState(false);
  const f = useForm({ password: "", confirm: "" });
  if (done)
    return (
      <div className="space-y-4">
        <Alert tone="ok">{t("done")}</Alert>
        <Button asChild>
          <Link href="/login">{t("signIn")}</Link>
        </Button>
      </div>
    );
  return (
    <form
      noValidate
      className="space-y-4"
      onSubmit={f.submit(
        (x) => ({ password: v.min(x.password, 8), confirm: v.match(x.password, x.confirm) }),
        async (x) => {
          await call(() => api.POST("/api/auth/reset-password", { body: { token, password: x.password } }));
          setDone(true);
        },
      )}
    >
      <FormError error={f.formError} />
      <Field label={t("password")} error={f.errors.password} required>
        {(p) => <PasswordInput {...p} {...f.text("password")} autoComplete="new-password" />}
      </Field>
      <Field label={t("confirm")} error={f.errors.confirm} required>
        {(p) => <PasswordInput {...p} {...f.text("confirm")} autoComplete="new-password" />}
      </Field>
      <Button type="submit" className="w-full" loading={f.busy}>
        {t("submit")}
      </Button>
    </form>
  );
}
