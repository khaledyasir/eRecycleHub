"use client";

import { useTranslations } from "next-intl";
import { useState, type ChangeEvent, type FormEvent } from "react";
import { useErrorMessage } from "./use-error-message";

type Values = Record<string, string | boolean | number[] | null>;
export type FieldErrors<T> = Partial<Record<keyof T, string>>;

/**
 * Small form state helper: values, per-field errors, one form-level (API) error and a busy flag.
 * Values are kept when the API rejects the submission, so users never retype.
 */
export function useForm<T extends Values>(initial: T, live?: (v: T) => FieldErrors<T>) {
  const message = useErrorMessage();
  const [values, setValues] = useState<T>(initial);
  const [errors, setErrors] = useState<FieldErrors<T>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const set = <K extends keyof T>(key: K, value: T[K]) => {
    setValues((v) => ({ ...v, [key]: value }));
    setErrors((e) => (e[key] ? { ...e, [key]: undefined } : e));
  };

  /** Props for a text-like input bound to `key`. */
  const text = (key: keyof T) => ({
    name: String(key),
    value: (values[key] as string) ?? "",
    onChange: (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => set(key, e.target.value as T[keyof T]),
    // With a `live` validator the field is checked as soon as the user leaves it (not only on submit), so a wrong value shows the error state at once.
    onBlur: () => {
      if (live) setErrors((e) => ({ ...e, [key]: live(values)[key] }));
    },
  });

  const submit = (validate: (v: T) => FieldErrors<T>, action: (v: T) => Promise<unknown>) => async (e: FormEvent) => {
    e.preventDefault();
    const errs = Object.fromEntries(Object.entries(validate(values)).filter(([, msg]) => msg)) as FieldErrors<T>;
    setErrors(errs);
    if (Object.keys(errs).length) {
      // Move focus to the first invalid control so keyboard and screen-reader users land on the problem.
      const first = Object.keys(errs)[0];
      (document.querySelector(`[name="${first}"]`) as HTMLElement | null)?.focus();
      return;
    }
    setBusy(true);
    setFormError(null);
    try {
      await action(values);
    } catch (err) {
      setFormError(message(err));
    } finally {
      setBusy(false);
    }
  };

  return { values, set, text, errors, setErrors, formError, setFormError, busy, submit };
}

/** Client-side checks with messages from errors.validation (the API re-validates everything). */
export function useValidators() {
  const t = useTranslations("errors.validation");
  return {
    required: (v: unknown) => (v === null || v === undefined || (typeof v === "string" && v.trim() === "") ? t("required") : undefined),
    email: (v: string) => (!v?.trim() ? t("required") : /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v.trim()) ? undefined : t("email")),
    min: (v: string, min: number) => (!v || v.length < min ? t("min", { min }) : undefined),
    match: (a: string, b: string) => (a !== b ? t("match") : undefined),
    amount: (v: string, { positive = true } = {}) => {
      if (!v?.trim()) return t("required");
      if (!/^\d+(\.\d{1,2})?$/.test(v.trim())) return t("amount");
      if (positive && Number(v) <= 0) return t("positive");
      return undefined;
    },
    /** Jordanian mobile: 07 followed by 8 digits, nothing else. */
    phone: (v: string) => (/^07\d{8}$/.test(v ?? "") ? undefined : t("phone")),
    /** Same as `phone` but a blank value is fine. */
    phoneOptional: (v: string) => (!v?.trim() ? undefined : /^07\d{8}$/.test(v) ? undefined : t("phone")),
    /** At least `min` characters and no spaces anywhere. */
    password: (v: string, min: number) => (!v || v.length < min ? t("min", { min }) : /\s/.test(v) ? t("noSpaces") : undefined),
    url: (v: string) => (!v?.trim() ? undefined : /^(https?:\/\/|\/[^/])/i.test(v.trim()) ? undefined : t("url")),
  };
}
