"use client";

import { Search } from "lucide-react";
import { useTranslations } from "next-intl";
import { useMemo, useState } from "react";
import { toast } from "sonner";
import { RichHtml } from "@/components/rich-html";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/display";
import { Field, Input, Textarea } from "@/components/ui/form";
import { api, call } from "@/lib/api/client";
import { useForm, useValidators } from "@/lib/use-form";

export function ContactForm() {
  const t = useTranslations("public.contact");
  const v = useValidators();
  const [sent, setSent] = useState(false);
  const f = useForm({ name: "", email: "", mobile: "", message: "" });
  if (sent) return <Alert tone="ok">{t("sent")}</Alert>;
  return (
    <form
      noValidate
      className="space-y-4"
      onSubmit={f.submit(
        (x) => ({ name: v.required(x.name), email: v.email(x.email), message: v.required(x.message) }),
        async (x) => {
          await call(() => api.POST("/api/contact", { body: { name: x.name, email: x.email, mobile: x.mobile || null, message: x.message } }));
          toast.success(t("sent"));
          setSent(true);
        },
      )}
    >
      {f.formError && <Alert tone="danger">{f.formError}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t("name")} error={f.errors.name} required>
          {(p) => <Input {...p} {...f.text("name")} autoComplete="name" />}
        </Field>
        <Field label={t("email")} error={f.errors.email} required>
          {(p) => <Input {...p} {...f.text("email")} type="email" autoComplete="email" />}
        </Field>
      </div>
      <Field label={t("mobile")}>{(p) => <Input {...p} {...f.text("mobile")} type="tel" autoComplete="tel" dir="ltr" />}</Field>
      <Field label={t("message")} error={f.errors.message} required>
        {(p) => <Textarea {...p} {...f.text("message")} maxLength={4000} rows={6} />}
      </Field>
      <Button type="submit" loading={f.busy}>
        {t("send")}
      </Button>
    </form>
  );
}

type Faq = { id: number; question: string; answer: string };
type FaqGroup = { id: number; title: string; faqs: Faq[] };

/** Accessible accordion (native details/summary) with a client-side filter. */
export function FaqList({ groups }: { groups: FaqGroup[] }) {
  const t = useTranslations("public.faq");
  const [q, setQ] = useState("");
  const filtered = useMemo(() => {
    const term = q.trim().toLowerCase();
    if (!term) return groups;
    return groups
      .map((g) => ({ ...g, faqs: g.faqs.filter((f) => f.question.toLowerCase().includes(term) || f.answer.toLowerCase().includes(term)) }))
      .filter((g) => g.faqs.length > 0);
  }, [groups, q]);
  return (
    <div className="space-y-8">
      <label className="relative block max-w-md">
        <span className="sr-only">{t("search")}</span>
        <Search className="pointer-events-none absolute start-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden />
        <Input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("search")} className="ps-9" />
      </label>
      {filtered.length === 0 && <p className="text-muted">{t("noMatch", { q })}</p>}
      {filtered.map((g) => (
        <section key={g.id} aria-labelledby={`faq-${g.id}`}>
          <h2 id={`faq-${g.id}`} className="mb-3 text-xl font-semibold">
            {g.title}
          </h2>
          <div className="divide-y divide-line overflow-hidden rounded-lg border border-line bg-surface">
            {g.faqs.map((f) => (
              <details key={f.id} className="group">
                <summary className="flex cursor-pointer list-none items-center justify-between gap-4 px-4 py-3 font-medium hover:bg-sunken [&::-webkit-details-marker]:hidden">
                  {f.question}
                  <span className="text-xl leading-none text-muted transition-transform group-open:rotate-45" aria-hidden>
                    +
                  </span>
                </summary>
                <RichHtml html={f.answer} className="px-4 pb-4" />
              </details>
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}
