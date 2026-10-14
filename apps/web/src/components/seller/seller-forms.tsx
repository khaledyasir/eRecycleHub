"use client";

import { Trash2 } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { CurrentFile } from "@/components/account/account-forms";
import { useAction } from "@/components/manage/manage-actions";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Field, FileInput, Input, Select } from "@/components/ui/form";
import { ConfirmDialog } from "@/components/ui/overlay";
import { useRouter } from "@/i18n/navigation";
import { sendForm, sendJson } from "@/lib/api/client";
import type { Schema } from "@/lib/api/types";
import { fileUrl } from "@/lib/format";
import { useForm, useValidators } from "@/lib/use-form";

export function DeleteReviewButton({ id }: { id: number }) {
  const t = useTranslations("seller.reviews");
  const run = useAction();
  return (
    <ConfirmDialog
      trigger={<Button variant="ghost" size="icon" aria-label={t("delete")}><Trash2 className="h-4 w-4 text-rust" /></Button>}
      title={t("deleteTitle")}
      body={t("deleteBody")}
      confirmLabel={t("delete")}
      onConfirm={() => run(() => sendJson("DELETE", `/api/seller/reviews/${id}`), t("deleted"))}
    />
  );
}

export function StoreProfileForm({ profile, countries }: { profile: Schema<"StoreProfileDto">; countries: { id: number; nameEn: string; nameAr: string }[] }) {
  const t = useTranslations("seller.profile");
  const ts = useTranslations("account.settings");
  const tc = useTranslations("common");
  const locale = useLocale();
  const v = useValidators();
  const router = useRouter();
  const f = useForm({
    firstName: profile.firstName, lastName: profile.lastName ?? "", address: profile.address ?? "", secondMobile: profile.secondMobile ?? "",
    countryId: String(profile.countryId), phone: profile.phone ?? "", companyName: profile.companyName ?? "", crNumber: profile.crNumber ?? "",
    vatNumber: profile.vatNumber ?? "", bankName: profile.bankName ?? "", accountNumber: profile.accountNumber ?? "", cardNumber: profile.cardNumber ?? "",
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
        (x) => ({ firstName: v.required(x.firstName), countryId: v.required(x.countryId) }),
        async (x) => {
          await sendForm("PUT", "/api/seller/profile", { ...x, countryId: Number(x.countryId) }, files);
          setFiles({});
          toast.success(t("saved"));
          router.refresh();
        },
      )}
    >
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}
      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{t("store")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("firstName")} error={f.errors.firstName} required>{(p) => <Input {...p} {...f.text("firstName")} />}</Field>
          <Field label={t("lastName")}>{(p) => <Input {...p} {...f.text("lastName")} />}</Field>
          <Field label={t("address")} className="sm:col-span-2">{(p) => <Input {...p} {...f.text("address")} />}</Field>
          <Field label={t("country")} error={f.errors.countryId} required>
            {(p) => (
              <Select {...p} {...f.text("countryId")}>
                {countries.map((c) => <option key={c.id} value={c.id}>{locale === "ar" ? c.nameAr : c.nameEn}</option>)}
              </Select>
            )}
          </Field>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          {(
            [
              ["logo", t("logo"), profile.logoFileId],
              ["background", t("background"), profile.backgroundFileId],
            ] as const
          ).map(([name, label, current]) => (
            <Field key={name} label={label}>
              {(p) => (
                <div className="flex flex-wrap items-center gap-3">
                  {current && (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img src={fileUrl(current)} alt="" className="h-12 w-16 rounded-md border border-line object-cover" />
                  )}
                  <FileInput {...p} accept={images} onFiles={pick(name)} />
                </div>
              )}
            </Field>
          ))}
        </div>
      </fieldset>
      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{t("contact")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("email")} hint={t("emailHint")}>{(p) => <Input {...p} value={profile.email} readOnly disabled />}</Field>
          <Field label={t("phone")}>{(p) => <Input {...p} {...f.text("phone")} type="tel" dir="ltr" />}</Field>
          <Field label={t("secondMobile")}>{(p) => <Input {...p} {...f.text("secondMobile")} type="tel" dir="ltr" />}</Field>
        </div>
      </fieldset>
      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{ts("company")}</legend>
        <Field label={ts("companyName")}>{(p) => <Input {...p} {...f.text("companyName")} />}</Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={ts("crNumber")}>{(p) => <Input {...p} {...f.text("crNumber")} dir="ltr" />}</Field>
          <Field label={ts("vatNumber")}>{(p) => <Input {...p} {...f.text("vatNumber")} dir="ltr" />}</Field>
          <Field label={ts("bankName")}>{(p) => <Input {...p} {...f.text("bankName")} />}</Field>
          <Field label={ts("accountNumber")}>{(p) => <Input {...p} {...f.text("accountNumber")} dir="ltr" />}</Field>
          <Field label={ts("cardNumber")}>{(p) => <Input {...p} {...f.text("cardNumber")} dir="ltr" inputMode="numeric" autoComplete="off" />}</Field>
        </div>
      </fieldset>
      <fieldset className="space-y-4">
        <legend className="text-lg font-semibold">{ts("documents")}</legend>
        <p className="text-sm text-muted">{ts("documentsHint")}</p>
        {(
          [
            ["crDocument", ts("crDocument"), profile.crDocument],
            ["vatCertificate", ts("vatCertificate"), profile.vatCertificate],
            ["idDocument", ts("idDocument"), profile.idDocument],
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
      </fieldset>
      <Button type="submit" loading={f.busy}>{tc("save")}</Button>
    </form>
  );
}
