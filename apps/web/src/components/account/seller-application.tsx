"use client";

import { Building2, UserRound } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Checkbox, Field, FileInput, Input, PhoneInput, Textarea } from "@/components/ui/form";
import { useRouter } from "@/i18n/navigation";
import { sendForm } from "@/lib/api/client";
import type { Schema } from "@/lib/api/types";
import { cn } from "@/lib/format";
import { useForm, useValidators } from "@/lib/use-form";

const DOCS = "image/jpeg,image/png,image/webp,application/pdf";
type AccountType = "Individual" | "Business";

/**
 * A signed-in customer applies to publish auctions, as an individual (national ID) or a business (company details and
 * commercial registration). Bank details are always required. The server re-checks everything; this form only guides the user.
 */
export function SellerApplicationForm({ categories, defaults, onFile }: {
  categories: Schema<"CategoryRef">[];
  defaults: { name: string; company: string; mobile: string; address: string; crNumber: string; vatNumber: string; bankName: string; accountHolderName: string; accountNumber: string };
  onFile: { cr?: string | null; vat?: string | null; id?: string | null };
}) {
  const t = useTranslations("account.seller");
  const locale = useLocale();
  const v = useValidators();
  const router = useRouter();
  const [type, setType] = useState<AccountType>("Business");
  const business = type === "Business";
  const f = useForm({ ...defaults, categoryIds: [] as number[] }, (x) => ({ mobile: v.phoneOptional(x.mobile) }));
  const [files, setFiles] = useState<Record<string, File | null>>({});
  const pick = (name: string) => (l: File[]) => setFiles((x) => ({ ...x, [name]: l[0] ?? null }));
  const [docError, setDocError] = useState<string | null>(null);

  return (
    <form
      noValidate
      className="space-y-6"
      onSubmit={f.submit(
        (x) => ({
          name: v.required(x.name), mobile: v.phoneOptional(x.mobile),
          ...(business ? { company: v.required(x.company), crNumber: v.required(x.crNumber) } : {}),
          bankName: v.required(x.bankName), accountHolderName: v.required(x.accountHolderName), accountNumber: v.required(x.accountNumber),
        }),
        async (x) => {
          // Client-side guidance only: the API enforces the same rule.
          if (business ? !(files.crDocument || onFile.cr) : !(files.idDocument || onFile.id)) {
            setDocError(business ? t("docsMissingBusiness") : t("docsMissingIndividual"));
            throw new Error("documents");
          }
          setDocError(null);
          await sendForm(
            "POST",
            "/api/account/seller-application",
            {
              accountType: type, name: x.name, company: business ? x.company : null, mobile: x.mobile, address: x.address, categoryIds: x.categoryIds,
              crNumber: business ? x.crNumber : null, vatNumber: business ? x.vatNumber : null, bankName: x.bankName, accountHolderName: x.accountHolderName, accountNumber: x.accountNumber,
            },
            business ? { crDocument: files.crDocument, vatCertificate: files.vatCertificate } : { idDocument: files.idDocument },
          );
          toast.success(t("sent"));
          router.refresh();
        },
      )}
    >
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}

      <fieldset>
        <legend className="mb-2 text-sm font-medium">{t("accountType")}</legend>
        <div className="grid gap-3 sm:grid-cols-2" role="radiogroup" aria-label={t("accountType")}>
          {(
            [
              ["Individual", UserRound, t("individual"), t("individualHint")],
              ["Business", Building2, t("business"), t("businessHint")],
            ] as const
          ).map(([value, Icon, label, hint]) => (
            <button
              key={value}
              type="button"
              role="radio"
              aria-checked={type === value}
              onClick={() => setType(value)}
              className={cn("flex items-start gap-3 rounded-xl border p-4 text-start transition", type === value ? "border-patina bg-patina-50 ring-2 ring-patina-100" : "border-line hover:border-patina/40")}
            >
              <span className={cn("grid h-10 w-10 shrink-0 place-items-center rounded-lg", type === value ? "bg-patina text-white" : "bg-sunken text-ink-2")}>
                <Icon className="h-5 w-5" aria-hidden />
              </span>
              <span>
                <span className="block font-bold">{label}</span>
                <span className="mt-0.5 block text-xs text-muted">{hint}</span>
              </span>
            </button>
          ))}
        </div>
      </fieldset>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t("storeName")} error={f.errors.name} required>
          {(p) => <Input {...p} {...f.text("name")} maxLength={200} />}
        </Field>
        <Field label={t("mobile")} error={f.errors.mobile}>{(p) => <PhoneInput {...p} {...f.text("mobile")} />}</Field>
        <Field label={t("address")} className="sm:col-span-2">{(p) => <Textarea {...p} {...f.text("address")} maxLength={1000} rows={2} />}</Field>
      </div>

      {business && (
        <fieldset className="space-y-4">
          <legend className="text-sm font-medium">{t("companyDetails")}</legend>
          <Field label={t("companyName")} error={f.errors.company} required>{(p) => <Input {...p} {...f.text("company")} maxLength={200} autoComplete="organization" />}</Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t("crNumber")} error={f.errors.crNumber} required>{(p) => <Input {...p} {...f.text("crNumber")} maxLength={100} dir="ltr" />}</Field>
            <Field label={t("vatNumber")}>{(p) => <Input {...p} {...f.text("vatNumber")} maxLength={100} dir="ltr" />}</Field>
          </div>
        </fieldset>
      )}

      <fieldset className="space-y-4">
        <legend className="text-sm font-medium">{t("bankDetails")}</legend>
        <p className="text-xs text-muted">{t("bankDetailsHint")}</p>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("bankName")} error={f.errors.bankName} required>{(p) => <Input {...p} {...f.text("bankName")} maxLength={200} />}</Field>
          <Field label={t("accountHolder")} error={f.errors.accountHolderName} required>{(p) => <Input {...p} {...f.text("accountHolderName")} maxLength={200} />}</Field>
        </div>
        <Field label={t("accountNumber")} error={f.errors.accountNumber} required>{(p) => <Input {...p} {...f.text("accountNumber")} maxLength={100} dir="ltr" />}</Field>
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
        {business ? (
          <>
            <Field label={t("crDocument")} required>
              {(p) => <FileInput {...p} accept={DOCS} onFiles={pick("crDocument")} current={onFile.cr ? t("onFile", { name: onFile.cr }) : null} />}
            </Field>
            <Field label={t("vatCertificate")}>
              {(p) => <FileInput {...p} accept={DOCS} onFiles={pick("vatCertificate")} current={onFile.vat ? t("onFile", { name: onFile.vat }) : null} />}
            </Field>
          </>
        ) : (
          <Field label={t("nationalId")} required>
            {(p) => <FileInput {...p} accept={DOCS} onFiles={pick("idDocument")} current={onFile.id ? t("onFile", { name: onFile.id }) : null} />}
          </Field>
        )}
      </fieldset>

      <Button type="submit" size="lg" loading={f.busy}>
        {t("submit")}
      </Button>
    </form>
  );
}
