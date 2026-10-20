"use client";

import { useTranslations, useLocale } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Checkbox, Field, FileInput, Input, Textarea } from "@/components/ui/form";
import { useRouter } from "@/i18n/navigation";
import { sendForm } from "@/lib/api/client";
import type { Schema } from "@/lib/api/types";
import { useForm, useValidators } from "@/lib/use-form";

const DOCS = "image/jpeg,image/png,image/webp,application/pdf";

/**
 * A signed-in customer applies to publish auctions. The commercial-registration and identity documents are required unless the
 * profile already holds them (the server re-checks everything; this form only guides the user).
 */
export function SellerApplicationForm({ categories, defaults, onFile }: {
  categories: Schema<"CategoryRef">[];
  defaults: { name: string; company: string; mobile: string; address: string; crNumber: string; vatNumber: string; bankName: string; accountNumber: string; cardNumber: string };
  onFile: { cr?: string | null; vat?: string | null; id?: string | null };
}) {
  const t = useTranslations("account.seller");
  const locale = useLocale();
  const v = useValidators();
  const router = useRouter();
  const f = useForm({ ...defaults, categoryIds: [] as number[] });
  const [files, setFiles] = useState<Record<string, File | null>>({});
  const pick = (name: string) => (l: File[]) => setFiles((x) => ({ ...x, [name]: l[0] ?? null }));
  const [docError, setDocError] = useState<string | null>(null);

  return (
    <form
      noValidate
      className="space-y-6"
      onSubmit={f.submit(
        (x) => ({
          name: v.required(x.name), company: v.required(x.company), crNumber: v.required(x.crNumber), bankName: v.required(x.bankName), accountNumber: v.required(x.accountNumber),
        }),
        async (x) => {
          // Client-side guidance only: the API enforces the same rule.
          if (!(files.crDocument || onFile.cr) || !(files.idDocument || onFile.id)) {
            setDocError(t("docsMissing"));
            throw new Error("documents");
          }
          setDocError(null);
          await sendForm("POST", "/api/account/seller-application", { name: x.name, company: x.company, mobile: x.mobile, address: x.address, categoryIds: x.categoryIds, crNumber: x.crNumber, vatNumber: x.vatNumber, bankName: x.bankName, accountNumber: x.accountNumber, cardNumber: x.cardNumber }, files);
          toast.success(t("sent"));
          router.refresh();
        },
      )}
    >
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t("storeName")} error={f.errors.name} required>
          {(p) => <Input {...p} {...f.text("name")} maxLength={200} />}
        </Field>
        <Field label={t("mobile")}>{(p) => <Input {...p} {...f.text("mobile")} maxLength={50} type="tel" dir="ltr" autoComplete="tel" />}</Field>
        <Field label={t("address")} className="sm:col-span-2">{(p) => <Textarea {...p} {...f.text("address")} maxLength={1000} rows={2} />}</Field>
      </div>

      <fieldset className="space-y-4">
        <legend className="text-sm font-medium">{t("companyDetails")}</legend>
        <Field label={t("companyName")} error={f.errors.company} required>{(p) => <Input {...p} {...f.text("company")} maxLength={200} autoComplete="organization" />}</Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("crNumber")} error={f.errors.crNumber} required>{(p) => <Input {...p} {...f.text("crNumber")} maxLength={100} dir="ltr" />}</Field>
          <Field label={t("vatNumber")}>{(p) => <Input {...p} {...f.text("vatNumber")} maxLength={100} dir="ltr" />}</Field>
        </div>
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="text-sm font-medium">{t("bankDetails")}</legend>
        <p className="text-xs text-muted">{t("bankDetailsHint")}</p>
        <Field label={t("bankName")} error={f.errors.bankName} required>{(p) => <Input {...p} {...f.text("bankName")} maxLength={200} />}</Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("accountNumber")} error={f.errors.accountNumber} required>{(p) => <Input {...p} {...f.text("accountNumber")} maxLength={100} dir="ltr" />}</Field>
          <Field label={t("cardNumber")}>{(p) => <Input {...p} {...f.text("cardNumber")} maxLength={100} dir="ltr" inputMode="numeric" autoComplete="off" />}</Field>
        </div>
      </fieldset>

      <fieldset>
        <legend className="text-sm font-medium">{t("categories")}</legend>
        <p className="mb-2 text-xs text-muted">{t("categoriesHint")}</p>
        {categories.length === 0 && <p className="text-sm text-muted">{t("noCategories")}</p>}
        <div className="grid gap-2 sm:grid-cols-2">
          {categories.map((c) => (
            <Checkbox
              key={c.id}
              label={locale === "ar" ? c.titleAr : c.titleEn}
              checked={f.values.categoryIds.includes(c.id)}
              onChange={(e) => f.set("categoryIds", e.target.checked ? [...f.values.categoryIds, c.id] : f.values.categoryIds.filter((id) => id !== c.id))}
            />
          ))}
        </div>
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="text-sm font-medium">{t("documents")}</legend>
        <p className="text-xs text-muted">{t("documentsHint")}</p>
        {docError && <Alert tone="danger">{docError}</Alert>}
        <Field label={t("crDocument")} required>
          {(p) => <FileInput {...p} accept={DOCS} onFiles={pick("crDocument")} current={onFile.cr ? t("onFile", { name: onFile.cr }) : null} />}
        </Field>
        <Field label={t("idDocument")} required>
          {(p) => <FileInput {...p} accept={DOCS} onFiles={pick("idDocument")} current={onFile.id ? t("onFile", { name: onFile.id }) : null} />}
        </Field>
        <Field label={t("vatCertificate")}>
          {(p) => <FileInput {...p} accept={DOCS} onFiles={pick("vatCertificate")} current={onFile.vat ? t("onFile", { name: onFile.vat }) : null} />}
        </Field>
      </fieldset>

      <Button type="submit" size="lg" loading={f.busy}>
        {t("submit")}
      </Button>
    </form>
  );
}
