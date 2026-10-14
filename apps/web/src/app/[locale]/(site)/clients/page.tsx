import type { Metadata } from "next";
import { getLocale, getTranslations } from "next-intl/server";
import { alternates } from "@/lib/seo";
import { Building2 } from "lucide-react";
import { ContentPage } from "@/components/public/content";
import { EmptyState } from "@/components/ui/display";
import { Pagination } from "@/components/ui/pagination";
import { load, serverApi } from "@/lib/api/server";
import { fileUrl } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  const [t, locale] = await Promise.all([getTranslations("public.clients"), getLocale()]);
  return { title: t("title"), description: t("description"), alternates: alternates(locale, "/clients") };
}

export default async function ClientsPage({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const sp = await searchParams;
  const [t, client] = await Promise.all([getTranslations("public.clients"), serverApi()]);
  const page = Math.max(1, Number(sp.page) || 1);
  const r = await load(client.GET("/api/clients", { params: { query: { page } } }));
  return (
    <ContentPage title={t("title")} description={t("description")} wide>
      {r.items.length === 0 ? (
        <EmptyState icon={<Building2 className="h-8 w-8" />} title={t("empty")} />
      ) : (
        <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
          {r.items.map((c) => (
            <li key={c.id} className="flex flex-col items-center gap-2 rounded-lg border border-line bg-surface p-4">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={fileUrl(c.avatarFileId)} alt={c.companyName ?? ""} loading="lazy" className="h-20 w-20 rounded-md object-contain" />
              {c.companyName && <span className="text-center text-sm text-ink-2">{c.companyName}</span>}
            </li>
          ))}
        </ul>
      )}
      <Pagination page={r.page} totalPages={Math.ceil(r.total / r.pageSize)} pathname="/clients" query={sp} />
    </ContentPage>
  );
}
