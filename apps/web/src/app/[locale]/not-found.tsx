import { SearchX } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Logo } from "@/components/layout/logo";
import { Link } from "@/i18n/navigation";

export default async function NotFound() {
  const t = await getTranslations("common.notFound");
  return (
    <main id="main" className="container-page flex min-h-dvh flex-col items-start justify-center gap-6 py-16">
      <Link href="/">
        <Logo />
      </Link>
      <SearchX className="h-10 w-10 text-muted" aria-hidden />
      <div>
        <h1 className="text-3xl font-semibold">{t("title")}</h1>
        <p className="mt-2 text-muted">{t("body")}</p>
      </div>
      <div className="flex flex-wrap gap-3">
        <Link href="/" className="inline-flex h-10 items-center rounded-md bg-patina px-4 text-sm font-medium text-white hover:bg-patina-700">
          {t("home")}
        </Link>
        <Link href="/auctions" className="inline-flex h-10 items-center rounded-md border border-line-strong bg-surface px-4 text-sm font-medium hover:bg-sunken">
          {t("auctions")}
        </Link>
      </div>
    </main>
  );
}
