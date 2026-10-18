import "server-only";
import { getLocale } from "next-intl/server";
import { redirect } from "@/i18n/navigation";
import { getSession } from "./api/server";
import type { SessionUser, UserRole } from "./api/types";

/**
 * Page-level guard for portal layouts. This is a navigation convenience only: every API endpoint enforces the same
 * rule on the server, so a page rendered for the wrong role would receive 401/403 for all of its data anyway.
 */
export async function requireRole(roles: UserRole[] | "any", loginPath = "/login"): Promise<SessionUser> {
  const [user, locale] = await Promise.all([getSession(), getLocale()]);
  if (!user) return redirect({ href: loginPath, locale }); // after sign-in the login page sends each role to its home
  if (roles !== "any" && !roles.includes(user.role)) {
    // Signed in with the wrong role: send them to their own home rather than a dead end.
    return redirect({ href: user.role === "Admin" ? "/admin" : "/account", locale });
  }
  return user;
}

/** Seller portal guard: a customer account that owns a publisher profile. Everyone else is sent to the page where they can apply. */
export async function requireSeller(): Promise<SessionUser> {
  const [user, locale] = await Promise.all([getSession(), getLocale()]);
  if (!user) return redirect({ href: "/login?next=/seller", locale });
  if (user.role === "Admin") return redirect({ href: "/admin", locale });
  if (user.publisherId == null) return redirect({ href: "/account/become-seller", locale });
  return user;
}
