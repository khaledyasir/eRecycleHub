import { Search } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { Link } from "@/i18n/navigation";
import { getCountryId, getSession, serverApi } from "@/lib/api/server";
import { Logo } from "./logo";
import { MobileNav, NavLinks } from "./nav";
import { CountrySwitcher, LocaleSwitcher } from "./switchers";
import { NotificationBell, UserMenu } from "./user-menu";

export async function SiteHeader() {
  const t = await getTranslations("common");
  const [user, countryId, client] = await Promise.all([getSession(), getCountryId(), serverApi()]);
  const countries = (await client.GET("/api/countries")).data ?? [];
  const links = [
    { href: "/auctions", label: t("nav.auctions") },
    { href: "/stores", label: t("nav.stores") },
    { href: "/how-to-buy", label: t("nav.howToBuy") },
    { href: "/about", label: t("nav.about") },
    { href: "/apply", label: t("nav.apply") },
  ];
  return (
    <header className="sticky top-0 z-40 border-b border-line bg-surface/95 backdrop-blur supports-[backdrop-filter]:bg-surface/85">
      <div className="hidden border-b border-line bg-ground md:block">
        <div className="container-page flex h-9 items-center justify-between gap-4 text-ink-2">
          <div className="flex items-center gap-3">
            <CountrySwitcher countries={countries} selected={countryId} />
            <Suspense>
              <LocaleSwitcher />
            </Suspense>
          </div>
          <nav className="flex items-center gap-4 text-sm" aria-label={t("footer.help")}>
            <Link href="/faq" className="hover:text-ink">
              {t("nav.faq")}
            </Link>
            <Link href="/contact" className="hover:text-ink">
              {t("nav.contact")}
            </Link>
          </nav>
        </div>
      </div>
      <div className="container-page flex h-16 items-center gap-4">
        <MobileNav links={links} countries={countries} countryId={countryId} signedIn={!!user} />
        <Link href="/" className="shrink-0" aria-label={t("nav.home")}>
          <Logo />
        </Link>
        <NavLinks links={links} label={t("nav.main")} />
        <form action="/auctions" role="search" className="ms-auto hidden max-w-xs flex-1 lg:block">
          <label className="relative block">
            <span className="sr-only">{t("search")}</span>
            <Search className="pointer-events-none absolute start-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden />
            <input
              name="q"
              type="search"
              placeholder={t("searchPlaceholder")}
              className="h-10 w-full rounded-md border border-line bg-ground ps-9 pe-3 text-sm placeholder:text-muted focus:border-patina-500 focus:bg-surface focus:outline-none focus:ring-2 focus:ring-patina-100"
            />
          </label>
        </form>
        <div className="ms-auto flex items-center gap-1 lg:ms-0">
          {user ? (
            <>
              {user.role !== "Admin" && <NotificationBell />}
              <UserMenu />
            </>
          ) : (
            <>
              <Link href="/login" className="hidden h-10 items-center rounded-md px-3 text-sm font-medium hover:bg-sunken sm:inline-flex">
                {t("user.signIn")}
              </Link>
              <Link href="/register" className="inline-flex h-10 items-center rounded-md bg-patina px-4 text-sm font-medium text-white hover:bg-patina-700">
                {t("user.register")}
              </Link>
            </>
          )}
        </div>
      </div>
    </header>
  );
}
