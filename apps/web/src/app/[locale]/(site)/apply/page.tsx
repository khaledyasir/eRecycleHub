import { getLocale } from "next-intl/server";
import { redirect } from "@/i18n/navigation";
import { getSession } from "@/lib/api/server";

/** Applying needs an account: send visitors to sign in (they return here), customers to their application page, admins home. */
export default async function ApplyPage() {
  const [user, locale] = await Promise.all([getSession(), getLocale()]);
  if (!user) return redirect({ href: "/login?next=/account/become-seller", locale });
  return redirect({ href: user.role === "Admin" ? "/admin" : "/account/become-seller", locale });
}
