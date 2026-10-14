import createMiddleware from "next-intl/middleware";
import { NextResponse, type NextRequest } from "next/server";
import { routing } from "./i18n/routing";

const intl = createMiddleware(routing);

/**
 * Legacy MVC URLs → new routes (permanent). IIS matched paths case-insensitively, so the old links can arrive in any case;
 * the few legacy paths that differ from a new route only by case (/Auctions, /Clients, /Stores) match their exact legacy
 * casing so the new lowercase routes are never captured. Targets are locale-less; next-intl then adds the visitor's locale.
 */
const LEGACY: [RegExp, string][] = [
  [/^\/default(\/index)?\/?$/i, "/"],
  [/^\/Auctions(\/Index)?\/?$/, "/auctions"],
  [/^\/auctions\/index\/?$/i, "/auctions"],
  [/^\/auctions\/details\/(\d+)\/?$/i, "/auctions/$1"],
  [/^\/(store|suppliers)\/list\/?$/i, "/stores"],
  [/^\/suppliers\/details\/?$/i, "/stores"],
  [/^\/store\/details\/(\d+)\/?$/i, "/stores/$1"],
  [/^\/Clients(\/Index)?\/?$/, "/clients"],
  [/^\/services(\/index)?\/?$/i, "/benefits"],
  [/^\/topic\/about\/?$/i, "/about"],
  [/^\/topic\/howtobuy\/?$/i, "/how-to-buy"],
  [/^\/topic\/howtosell\/?$/i, "/how-to-sell"],
  [/^\/topic\/faqs\/?$/i, "/faq"],
  [/^\/topic\/supportcenter\/?$/i, "/support"],
  [/^\/topic\/contact\/?$/i, "/contact"],
  [/^\/topic\/advertise\/?$/i, "/advertise"],
  [/^\/topic\/condition\/?$/i, "/terms"],
  [/^\/topic\/privacypolicy\/?$/i, "/privacy"],
  [/^\/topic\/favorite\/?$/i, "/account/favorites"],
  [/^\/topic\/notifyme\/?$/i, "/account/watching"],
  [/^\/topic\/notifications\/?$/i, "/account/notifications"],
  [/^\/(account|registration)\/login\/?$/i, "/login"],
  [/^\/(account|registration)\/register\/?$/i, "/register"],
  [/^\/account\/passwordrecovery\/?$/i, "/forgot-password"],
  [/^\/account\/resetpassword\/?$/i, "/reset-password"],
  [/^\/account\/subscribe\/?$/i, "/apply"],
  [/^\/account\/profile\/?$/i, "/account"],
  [/^\/account\/myauction\/?$/i, "/account/auctions"],
  [/^\/account\/calanderauction\/?$/i, "/account/calendar"],
  [/^\/account\/transactions\/?$/i, "/account/wallet"],
  [/^\/account\/settings\/?$/i, "/account/settings"],
  [/^\/account\/changepassword\/?$/i, "/account/password"],
  [/^\/dashboard\/cmslogin\/?$/i, "/admin/login"],
  [/^\/dashboard(\/.*)?$/i, "/admin"],
  [/^\/Stores(\/.*)?$/, "/seller"],
];

export default function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  for (const [pattern, target] of LEGACY) {
    const m = pathname.match(pattern);
    if (m) {
      const url = request.nextUrl.clone();
      url.pathname = target.replace("$1", m[1] ?? "");
      // Keep the query only where it still means something (the reset link's token).
      url.search = target === "/reset-password" ? search : "";
      return NextResponse.redirect(url, 308);
    }
  }
  return intl(request);
}

export const config = {
  // Everything except the API proxy, Next internals and files with an extension.
  matcher: "/((?!api|_next|_vercel|.*\\..*).*)",
};
