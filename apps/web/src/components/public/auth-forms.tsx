"use client";

import { Check, Circle, Eye, EyeOff } from "lucide-react";
import { useTranslations } from "next-intl";
import { useState, type ReactNode } from "react";
import { toast } from "sonner";
import { WELCOME_KEY } from "@/components/account/welcome-prompt";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Field, Input, PhoneInput } from "@/components/ui/form";
import { Link, useRouter } from "@/i18n/navigation";
import { api, call, sendForm, setCsrfToken } from "@/lib/api/client";
import type { Schema, UserRole } from "@/lib/api/types";
import { cn } from "@/lib/format";
import { useErrorMessage } from "@/lib/use-error-message";
import { passwordCriteria, useForm, useValidators } from "@/lib/use-form";

type Me = Schema<"MeResponse">;

export const homeFor = (role: UserRole) => (role === "Admin" ? "/admin" : "/account");
/** Only same-site relative paths are followed after sign-in (no open redirect). */
export const safeNext = (next: string | null | undefined) => {
  if (typeof next !== "string" || !next.startsWith("/") || next.startsWith("//") || /[\\\u0000-\u001f\u007f]/.test(next)) return null;
  const url = new URL(next, "https://local.invalid");
  if (url.origin !== "https://local.invalid" || /^\/(?:ar\/|en\/)?login\/?$/i.test(url.pathname)) return null;
  return next;
};

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

/** Live checklist of the new-password rule, shown under the field (the app's criteria card). */
export function PasswordChecklist({ value }: { value: string }) {
  const t = useTranslations("auth.criteria");
  const met = passwordCriteria(value);
  return (
    <ul className="space-y-0.5 text-xs" aria-label={t("title")}>
      {(["length", "case", "number", "symbol"] as const).map((k) => (
        <li key={k} className={cn("flex items-center gap-1.5", met[k] ? "font-medium text-ok" : "text-muted")}>
          {met[k] ? <Check className="h-3.5 w-3.5 shrink-0" aria-hidden /> : <Circle className="h-3.5 w-3.5 shrink-0" aria-hidden />}
          {t(k)}
        </li>
      ))}
    </ul>
  );
}

/** Verification code length: OtpOptions.Length and Otp:FixedCode on the API. */
const OTP_LENGTH = 5;

function FormError({ error }: { error: string | null }) {
  return error ? <Alert tone="danger">{error}</Alert> : null;
}

export function LoginForm({ next, adminOnly = false }: { next?: string; adminOnly?: boolean }) {
  const t = useTranslations("auth.login");
  const v = useValidators();
  const router = useRouter();
  const f = useForm({ email: "", password: "" }, (x) => ({ email: v.email(x.email) }));
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

export function RegisterForm() {
  const t = useTranslations("auth.register");
  const v = useValidators();
  const router = useRouter();
  // Company, bank and document details are collected later, when the customer applies to become a seller.
  // Jordan is the only country: the API assigns it, so the form has no country.
  const f = useForm(
    { fullName: "", email: "", phone: "", password: "", confirm: "" },
    (x) => ({ email: v.email(x.email), phone: v.phone(x.phone), password: v.strongPassword(x.password), confirm: x.confirm ? v.match(x.password, x.confirm) : undefined }),
  );
  return (
    <form
      noValidate
      className="space-y-8"
      onSubmit={f.submit(
        (x) => ({
          fullName: v.required(x.fullName), email: v.email(x.email), phone: v.phone(x.phone),
          password: v.strongPassword(x.password), confirm: v.match(x.password, x.confirm),
        }),
        async (x) => {
          const { confirm: _c, ...rest } = x;
          void _c;
          const me = await sendForm<Me>("POST", "/api/auth/register", rest, {});
          setCsrfToken(me.csrfToken);
          try { localStorage.setItem(WELCOME_KEY, "1"); } catch { /* storage blocked */ }
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
        <div className="space-y-4">
          <Field label={t("email")} error={f.errors.email} required>
            {(p) => <Input {...p} {...f.text("email")} type="email" autoComplete="email" />}
          </Field>
          <Field label={t("phone")} error={f.errors.phone} required>
            {(p) => <PhoneInput {...p} {...f.text("phone")} />}
          </Field>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("password")} error={f.errors.password} required>
            {(p) => (
              <>
                <PasswordInput {...p} {...f.text("password")} autoComplete="new-password" />
                <PasswordChecklist value={f.values.password} />
              </>
            )}
          </Field>
          <Field label={t("confirm")} error={f.errors.confirm} required>
            {(p) => <PasswordInput {...p} {...f.text("confirm")} autoComplete="new-password" />}
          </Field>
        </div>
      </fieldset>

      <Button type="submit" size="lg" className="w-full" loading={f.busy}>
        {t("submit")}
      </Button>
    </form>
  );
}

/** Forgot password: a reset link by e-mail, or a code on the phone (like the app). */
export function ForgotForm() {
  const t = useTranslations("auth.forgot");
  const [byPhone, setByPhone] = useState(false);
  return (
    <div className="space-y-5">
      <div role="group" aria-label={t("method")} className="grid grid-cols-2 gap-2">
        {[false, true].map((phone) => (
          <Button key={String(phone)} type="button" variant={byPhone === phone ? "primary" : "secondary"} aria-pressed={byPhone === phone} onClick={() => setByPhone(phone)}>
            {t(phone ? "byPhone" : "byEmail")}
          </Button>
        ))}
      </div>
      {byPhone ? <ForgotByPhone /> : <ForgotByEmail />}
    </div>
  );
}

/** Phone number, then the code, then a new password; ends on the sign-in page with a success message. */
function ForgotByPhone() {
  const t = useTranslations("auth.forgot");
  const tr = useTranslations("auth.reset");
  const v = useValidators();
  const router = useRouter();
  const message = useErrorMessage();
  const [step, setStep] = useState<"phone" | "code" | "password">("phone");
  const [token, setToken] = useState("");
  const [resending, setResending] = useState(false);
  const f = useForm({ phone: "", code: "", password: "", confirm: "" }, (x) => ({ phone: v.phone(x.phone) }));
  const send = (phone: string) => call(() => api.POST("/api/auth/otp/send", { body: { phone } }));

  if (step === "phone")
    return (
      <form
        noValidate
        className="space-y-4"
        onSubmit={f.submit(
          (x) => ({ phone: v.phone(x.phone) }),
          async (x) => {
            await send(x.phone);
            setStep("code");
          },
        )}
      >
        <FormError error={f.formError} />
        <Field label={t("phone")} error={f.errors.phone} required>
          {(p) => <PhoneInput {...p} {...f.text("phone")} />}
        </Field>
        <Button type="submit" className="w-full" loading={f.busy}>
          {t("sendCode")}
        </Button>
      </form>
    );

  if (step === "code")
    return (
      <form
        noValidate
        className="space-y-4"
        onSubmit={f.submit(
          (x) => ({ code: x.code.length === OTP_LENGTH && /^[0-9]+$/.test(x.code) ? undefined : t("codeInvalid", { length: OTP_LENGTH }) }),
          async (x) => {
            const r = await call(() => api.POST("/api/auth/otp/verify", { body: { phone: x.phone, code: x.code } }));
            setToken(r.resetToken);
            setStep("password");
          },
        )}
      >
        <FormError error={f.formError} />
        <Field label={t("code")} hint={t("codeSentTo", { phone: f.values.phone })} error={f.errors.code} required>
          {(p) => <Input {...p} {...f.text("code")} inputMode="numeric" autoComplete="one-time-code" maxLength={OTP_LENGTH} dir="ltr" className="tabular max-w-40 tracking-widest" />}
        </Field>
        <div className="flex items-center justify-between gap-3">
          <Button
            type="button"
            variant="ghost"
            loading={resending}
            onClick={async () => {
              setResending(true);
              f.setFormError(null);
              try {
                await send(f.values.phone);
                toast.success(t("codeSentTo", { phone: f.values.phone }));
              } catch (e) {
                f.setFormError(message(e));
              } finally {
                setResending(false);
              }
            }}
          >
            {t("resend")}
          </Button>
          <Button type="submit" loading={f.busy}>
            {t("verify")}
          </Button>
        </div>
      </form>
    );

  return (
    <form
      noValidate
      className="space-y-4"
      onSubmit={f.submit(
        (x) => ({ password: v.strongPassword(x.password), confirm: v.match(x.password, x.confirm) }),
        async (x) => {
          await call(() => api.POST("/api/auth/reset-password", { body: { token, password: x.password } }));
          router.push("/login?reset=1");
        },
      )}
    >
      <FormError error={f.formError} />
      <Field label={tr("password")} error={f.errors.password} required>
        {(p) => (
          <>
            <PasswordInput {...p} {...f.text("password")} autoComplete="new-password" />
            <PasswordChecklist value={f.values.password} />
          </>
        )}
      </Field>
      <Field label={tr("confirm")} error={f.errors.confirm} required>
        {(p) => <PasswordInput {...p} {...f.text("confirm")} autoComplete="new-password" />}
      </Field>
      <Button type="submit" className="w-full" loading={f.busy}>
        {tr("submit")}
      </Button>
    </form>
  );
}

function ForgotByEmail() {
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
        (x) => ({ password: v.strongPassword(x.password), confirm: v.match(x.password, x.confirm) }),
        async (x) => {
          await call(() => api.POST("/api/auth/reset-password", { body: { token, password: x.password } }));
          setDone(true);
        },
      )}
    >
      <FormError error={f.formError} />
      <Field label={t("password")} error={f.errors.password} required>
        {(p) => (
          <>
            <PasswordInput {...p} {...f.text("password")} autoComplete="new-password" />
            <PasswordChecklist value={f.values.password} />
          </>
        )}
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
