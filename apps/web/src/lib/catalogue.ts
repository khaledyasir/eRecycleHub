import "server-only";

/** The shipped message catalogues, flattened to "area.path.to.key" -> text (what the Translations page lists and overrides). */

import en_common from "../messages/en/common.json";
import en_errors from "../messages/en/errors.json";
import en_public from "../messages/en/public.json";
import en_auction from "../messages/en/auction.json";
import en_auth from "../messages/en/auth.json";
import en_account from "../messages/en/account.json";
import en_seller from "../messages/en/seller.json";
import en_admin from "../messages/en/admin.json";
import en_cms from "../messages/en/cms.json";
import ar_common from "../messages/ar/common.json";
import ar_errors from "../messages/ar/errors.json";
import ar_public from "../messages/ar/public.json";
import ar_auction from "../messages/ar/auction.json";
import ar_auth from "../messages/ar/auth.json";
import ar_account from "../messages/ar/account.json";
import ar_seller from "../messages/ar/seller.json";
import ar_admin from "../messages/ar/admin.json";
import ar_cms from "../messages/ar/cms.json";

const shipped: Record<string, Record<string, unknown>> = {
  en: { common: en_common, errors: en_errors, public: en_public, auction: en_auction, auth: en_auth, account: en_account, seller: en_seller, admin: en_admin, cms: en_cms },
  ar: { common: ar_common, errors: ar_errors, public: ar_public, auction: ar_auction, auth: ar_auth, account: ar_account, seller: ar_seller, admin: ar_admin, cms: ar_cms },
};

function flatten(node: unknown, prefix: string, out: Record<string, string>) {
  if (typeof node === "string") out[prefix] = node;
  else if (node && typeof node === "object") for (const [k, v] of Object.entries(node)) flatten(v, prefix ? `${prefix}.${k}` : k, out);
}

export function shippedTexts(locale: "en" | "ar"): Record<string, string> {
  const out: Record<string, string> = {};
  flatten(shipped[locale], "", out);
  return out;
}
