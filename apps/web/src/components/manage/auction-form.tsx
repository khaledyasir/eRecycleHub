"use client";

import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { CurrentFile } from "@/components/account/account-forms";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Checkbox, Field, FileInput, Input, Select, Textarea } from "@/components/ui/form";
import { Switch } from "@/components/ui/controls";
import { RichTextEditor } from "@/components/ui/rich-text";
import { useRouter } from "@/i18n/navigation";
import { api, call, sendForm } from "@/lib/api/client";
import type { Schema } from "@/lib/api/types";
import { DISPLAY_TZ, fileUrl, fromLocalInput, toLocalInput } from "@/lib/format";
import { useForm, useValidators } from "@/lib/use-form";

export type ManageBase = "/api/seller" | "/api/admin";
type Country = { id: number; nameEn: string; nameAr: string };
type Spec = { titleEn: string; titleAr: string; valueEn: string; valueAr: string };

const inDays = (d: number) => new Date(Date.now() + d * 86_400_000).toISOString();

/**
 * Create/edit form shared by the seller portal and the admin panel. The API enforces every rule (ownership, frozen terms once bids exist,
 * dates, amounts); this form mirrors them so mistakes are caught before submitting.
 */
export function AuctionForm({ base, admin, countries, publishers, auction, defaultCountryId, routeBase }: {
  base: ManageBase;
  admin: boolean;
  countries: Country[];
  publishers?: { id: number; titleEn: string }[];
  auction?: Schema<"ManagedAuction">;
  defaultCountryId?: number;
  routeBase: "/seller/auctions" | "/admin/auctions";
}) {
  const t = useTranslations("seller.manage.form");
  const tm = useTranslations("seller.manage");
  const te = useTranslations("errors.validation");
  const tea = useTranslations("errors.auction");
  const tc = useTranslations("common");
  const locale = useLocale();
  const v = useValidators();
  const router = useRouter();
  const frozen = (auction?.bidCount ?? 0) > 0;

  const f = useForm({
    countryId: String(auction?.countryId ?? defaultCountryId ?? countries[0]?.id ?? ""),
    categoryId: auction?.categoryId ? String(auction.categoryId) : "",
    publisherId: auction?.publisherId ? String(auction.publisherId) : "",
    titleEn: auction?.titleEn ?? "",
    titleAr: auction?.titleAr ?? "",
    shortDescEn: auction?.shortDescEn ?? "",
    shortDescAr: auction?.shortDescAr ?? "",
    descEn: auction?.descEn ?? "",
    descAr: auction?.descAr ?? "",
    startPrice: auction ? String(auction.startPrice) : "",
    bidIncrement: auction ? String(auction.bidIncrement) : "",
    insuranceAmount: auction ? String(auction.insuranceAmount) : "0",
    startsAt: toLocalInput(auction?.startsAtUtc ?? inDays(0)),
    endsAt: toLocalInput(auction?.endsAtUtc ?? inDays(7)),
    videoUrl: auction?.videoUrl ?? "",
    isActive: auction?.isActive ?? true,
  });
  const [specs, setSpecs] = useState<Spec[]>(
    auction?.specs.map((s) => ({ titleEn: s.titleEn, titleAr: s.titleAr, valueEn: s.valueEn ?? "", valueAr: s.valueAr ?? "" })) ?? [],
  );
  const [files, setFiles] = useState<Record<string, File | File[] | null>>({});
  const [remove, setRemove] = useState({ terms: false, paymentConditions: false, contract: false });
  const [categories, setCategories] = useState<Schema<"CategoryRef">[]>([]);

  useEffect(() => {
    const id = Number(f.values.countryId);
    if (!id) return;
    let cancelled = false;
    call(() => api.GET("/api/categories", { params: { query: { countryId: id } } }))
      .then((list) => !cancelled && setCategories(list))
      .catch(() => !cancelled && setCategories([]));
    return () => {
      cancelled = true;
    };
  }, [f.values.countryId]);

  const setSpec = (i: number, key: keyof Spec, value: string) => setSpecs((s) => s.map((x, n) => (n === i ? { ...x, [key]: value } : x)));
  const move = (i: number, d: -1 | 1) => setSpecs((s) => {
    const j = i + d;
    if (j < 0 || j >= s.length) return s;
    const copy = [...s];
    [copy[i], copy[j]] = [copy[j], copy[i]];
    return copy;
  });

  const validate = (x: typeof f.values) => {
    const errs: Partial<Record<keyof typeof x, string>> = {
      titleEn: !x.titleEn.trim() && !x.titleAr.trim() ? te("required") : undefined,
      countryId: v.required(x.countryId),
      startPrice: v.amount(x.startPrice),
      bidIncrement: v.amount(x.bidIncrement),
      insuranceAmount: v.amount(x.insuranceAmount, { positive: false }),
      startsAt: x.startsAt ? undefined : te("date"),
      endsAt: !x.endsAt ? te("date") : x.startsAt && x.endsAt <= x.startsAt ? te("endAfterStart") : undefined,
      videoUrl: v.url(x.videoUrl),
    };
    return errs;
  };

  return (
    <form
      noValidate
      className="space-y-8"
      onSubmit={f.submit(validate, async (x) => {
        if (!auction && !files.mainImage) {
          f.setFormError(tea("main_image_required"));
          return;
        }
        const data = {
          countryId: Number(x.countryId),
          categoryId: x.categoryId ? Number(x.categoryId) : null,
          publisherId: admin ? (x.publisherId ? Number(x.publisherId) : null) : null,
          titleEn: x.titleEn, titleAr: x.titleAr, shortDescEn: x.shortDescEn, shortDescAr: x.shortDescAr, descEn: x.descEn, descAr: x.descAr,
          startPrice: Number(x.startPrice), bidIncrement: Number(x.bidIncrement), insuranceAmount: Number(x.insuranceAmount),
          startsAtUtc: fromLocalInput(x.startsAt), endsAtUtc: fromLocalInput(x.endsAt),
          isActive: x.isActive, videoUrl: x.videoUrl || null,
          specs: specs.filter((s) => s.titleEn.trim() || s.titleAr.trim()),
          removeTerms: remove.terms, removePaymentConditions: remove.paymentConditions, removeContract: remove.contract,
        };
        if (auction) {
          await sendForm("PUT", `${base}/auctions/${auction.id}`, data, files);
          setFiles({});
          toast.success(tm("saved"));
          router.refresh();
        } else {
          const r = await sendForm<{ id: number }>("POST", `${base}/auctions`, data, files);
          toast.success(tm("created"));
          router.push(`${routeBase}/${r.id}`);
        }
      })}
    >
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}
      {frozen && <Alert tone="info">{t("frozen", { count: auction!.bidCount })}</Alert>}

      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{t("basics")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("country")} error={f.errors.countryId} required>
            {(p) => (
              <Select {...p} value={f.values.countryId} disabled={frozen} onChange={(e) => { f.set("countryId", e.target.value); f.set("categoryId", ""); }}>
                {countries.map((c) => (
                  <option key={c.id} value={c.id}>{locale === "ar" ? c.nameAr : c.nameEn}</option>
                ))}
              </Select>
            )}
          </Field>
          <Field label={t("category")}>
            {(p) => (
              <Select {...p} {...f.text("categoryId")}>
                <option value="">{t("noCategory")}</option>
                {(Number(f.values.countryId) ? categories : []).map((c) => (
                  <option key={c.id} value={c.id}>{locale === "ar" ? c.titleAr : c.titleEn}</option>
                ))}
                {auction?.categoryId && !categories.some((c) => c.id === auction.categoryId) && <option value={auction.categoryId}>#{auction.categoryId}</option>}
              </Select>
            )}
          </Field>
          {admin && (
            <Field label={t("publisher")} hint={t("publisherHint")} className="sm:col-span-2">
              {(p) => (
                <Select {...p} {...f.text("publisherId")} disabled={frozen}>
                  <option value="">{tm("platform")}</option>
                  {publishers?.map((x) => (
                    <option key={x.id} value={x.id}>{x.titleEn}</option>
                  ))}
                </Select>
              )}
            </Field>
          )}
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("titleEn")} hint={t("titleHint")} error={f.errors.titleEn}>
            {(p) => <Input {...p} {...f.text("titleEn")} maxLength={300} dir="ltr" />}
          </Field>
          <Field label={t("titleAr")}>{(p) => <Input {...p} {...f.text("titleAr")} maxLength={300} dir="rtl" />}</Field>
          <Field label={t("shortEn")}>{(p) => <Textarea {...p} {...f.text("shortDescEn")} rows={2} dir="ltr" />}</Field>
          <Field label={t("shortAr")}>{(p) => <Textarea {...p} {...f.text("shortDescAr")} rows={2} dir="rtl" />}</Field>
        </div>
        <div className="grid gap-4 lg:grid-cols-2">
          <Field label={t("descEn")}>{(p) => <RichTextEditor {...p} value={f.values.descEn} onChange={(html) => f.set("descEn", html)} dir="ltr" />}</Field>
          <Field label={t("descAr")}>{(p) => <RichTextEditor {...p} value={f.values.descAr} onChange={(html) => f.set("descAr", html)} dir="rtl" />}</Field>
        </div>
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{t("pricing")}</legend>
        <div className="grid gap-4 sm:grid-cols-3">
          <Field label={t("startPrice")} error={f.errors.startPrice} required>
            {(p) => <Input {...p} {...f.text("startPrice")} inputMode="decimal" dir="ltr" className="tabular" disabled={frozen} />}
          </Field>
          <Field label={t("increment")} hint={t("incrementHint")} error={f.errors.bidIncrement} required>
            {(p) => <Input {...p} {...f.text("bidIncrement")} inputMode="decimal" dir="ltr" className="tabular" disabled={frozen} />}
          </Field>
          <Field label={t("insurance")} hint={t("insuranceHint")} error={f.errors.insuranceAmount} required>
            {(p) => <Input {...p} {...f.text("insuranceAmount")} inputMode="decimal" dir="ltr" className="tabular" disabled={frozen} />}
          </Field>
        </div>
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{t("schedule")}</legend>
        <p className="text-sm text-muted">{t("zone", { zone: DISPLAY_TZ })}</p>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("startsAt")} error={f.errors.startsAt} required>
            {(p) => <Input {...p} {...f.text("startsAt")} type="datetime-local" disabled={frozen} />}
          </Field>
          <Field label={t("endsAt")} error={f.errors.endsAt} required>
            {(p) => <Input {...p} {...f.text("endsAt")} type="datetime-local" />}
          </Field>
        </div>
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="mb-2 text-lg font-semibold">{t("media")}</legend>
        <Field label={t("mainImage")} hint={t("mainImageHint")} required={!auction}>
          {(p) => (
            <div className="flex flex-wrap items-center gap-3">
              {auction?.mainImageFileId && (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={fileUrl(auction.mainImageFileId)} alt="" className="h-16 w-20 rounded-md border border-line object-cover" />
              )}
              <FileInput {...p} accept="image/jpeg,image/png,image/webp" onFiles={(l) => setFiles((x) => ({ ...x, mainImage: l[0] ?? null }))} />
            </div>
          )}
        </Field>
        {!auction && (
          <Field label={t("gallery")} hint={t("galleryHint")}>
            {(p) => <FileInput {...p} multiple accept="image/jpeg,image/png,image/webp" onFiles={(l) => setFiles((x) => ({ ...x, images: l }))} />}
          </Field>
        )}
        <Field label={t("videoUrl")} error={f.errors.videoUrl}>{(p) => <Input {...p} {...f.text("videoUrl")} type="url" placeholder="https://" dir="ltr" />}</Field>
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="text-lg font-semibold">{t("documents")}</legend>
        <p className="text-sm text-muted">{t("documentsHint")}</p>
        {(
          [
            ["terms", t("terms"), auction?.terms],
            ["paymentConditions", t("paymentConditions"), auction?.paymentConditions],
            ["contract", t("contract"), auction?.contract],
          ] as const
        ).map(([name, label, current]) => (
          <Field key={name} label={label}>
            {(p) => (
              <div className="space-y-1">
                <FileInput {...p} accept="application/pdf,.doc,.docx,.xls,.xlsx" onFiles={(l) => setFiles((x) => ({ ...x, [name]: l[0] ?? null }))} />
                {current && (
                  <div className="flex flex-wrap items-center gap-3">
                    <CurrentFile file={current} label={t("current", { name: current.name })} />
                    <Checkbox label={t("remove")} checked={remove[name]} onChange={(e) => setRemove((r) => ({ ...r, [name]: e.target.checked }))} />
                  </div>
                )}
              </div>
            )}
          </Field>
        ))}
      </fieldset>

      <fieldset className="space-y-3">
        <legend className="text-lg font-semibold">{t("specs")}</legend>
        <p className="text-sm text-muted">{t("specsHint")}</p>
        {specs.map((s, i) => (
          <div key={i} className="grid gap-2 rounded-lg border border-line bg-ground p-3 sm:grid-cols-[1fr_1fr_auto]">
            <Input aria-label={t("specTitleEn")} placeholder={t("specTitleEn")} value={s.titleEn} onChange={(e) => setSpec(i, "titleEn", e.target.value)} dir="ltr" />
            <Input aria-label={t("specTitleAr")} placeholder={t("specTitleAr")} value={s.titleAr} onChange={(e) => setSpec(i, "titleAr", e.target.value)} dir="rtl" />
            <div className="row-span-2 flex items-start gap-1 sm:flex-col">
              <Button type="button" variant="ghost" size="icon" onClick={() => move(i, -1)} aria-label={t("moveUp")} disabled={i === 0}><ArrowUp className="h-4 w-4" /></Button>
              <Button type="button" variant="ghost" size="icon" onClick={() => move(i, 1)} aria-label={t("moveDown")} disabled={i === specs.length - 1}><ArrowDown className="h-4 w-4" /></Button>
              <Button type="button" variant="ghost" size="icon" onClick={() => setSpecs((x) => x.filter((_, n) => n !== i))} aria-label={t("removeSpec")}><Trash2 className="h-4 w-4 text-rust" /></Button>
            </div>
            <Input aria-label={t("specValueEn")} placeholder={t("specValueEn")} value={s.valueEn} onChange={(e) => setSpec(i, "valueEn", e.target.value)} dir="ltr" />
            <Input aria-label={t("specValueAr")} placeholder={t("specValueAr")} value={s.valueAr} onChange={(e) => setSpec(i, "valueAr", e.target.value)} dir="rtl" />
          </div>
        ))}
        <Button type="button" variant="secondary" size="sm" onClick={() => setSpecs((x) => [...x, { titleEn: "", titleAr: "", valueEn: "", valueAr: "" }])} disabled={specs.length >= 50}>
          <Plus className="h-4 w-4" aria-hidden /> {t("addSpec")}
        </Button>
      </fieldset>

      <fieldset className="space-y-2">
        <legend className="mb-1 text-lg font-semibold">{t("visibility")}</legend>
        <div className="flex items-center gap-3">
          <Switch checked={f.values.isActive} onCheckedChange={(on) => f.set("isActive", on)} label={t("isActive")} id="auction-active" />
          <label htmlFor="auction-active" className="text-sm font-medium">{t("isActive")}</label>
        </div>
        <p className="text-xs text-muted">{t("isActiveHint")}</p>
      </fieldset>

      <div className="sticky bottom-0 -mx-5 border-t border-line bg-surface/95 px-5 py-3 backdrop-blur sm:-mx-7 sm:px-7">
        <Button type="submit" size="lg" loading={f.busy}>
          {auction ? tc("save") : tm("create")}
        </Button>
      </div>
    </form>
  );
}
