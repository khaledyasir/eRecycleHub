import { describe, expect, it } from "vitest";
import { shippedTexts } from "@/lib/catalogue";

const en = shippedTexts("en");
const ar = shippedTexts("ar");

/** Names of ICU arguments ({name}, {count, plural, ...}) used by a message, ignoring text inside plural branches. */
function argumentNames(message: string): string[] {
  const names = new Set<string>();
  let depth = 0;
  let token = "";
  for (let i = 0; i < message.length; i++) {
    const ch = message[i];
    if (ch === "'" && message[i + 1] === "{") { i += 2; continue; } // quoted literal brace
    if (ch === "{") {
      if (depth === 0) token = "";
      depth++;
    } else if (ch === "}") {
      depth--;
      if (depth === 0 && token) names.add(token.split(",")[0].trim());
    } else if (depth >= 1) {
      if (depth === 1) token += ch;
    }
  }
  return [...names].filter((n) => /^[A-Za-z_]\w*$/.test(n)).sort();
}

describe("message catalogues", () => {
  it("English and Arabic have exactly the same keys", () => {
    const missingInAr = Object.keys(en).filter((k) => !(k in ar));
    const missingInEn = Object.keys(ar).filter((k) => !(k in en));
    expect({ missingInAr, missingInEn }).toEqual({ missingInAr: [], missingInEn: [] });
  });

  it("has no empty texts", () => {
    const empty = [...Object.entries(en), ...Object.entries(ar)].filter(([, v]) => !v.trim()).map(([k]) => k);
    expect(empty).toEqual([]);
  });

  it("uses the same placeholders in both languages", () => {
    const mismatched = Object.keys(en)
      .filter((k) => k in ar && JSON.stringify(argumentNames(en[k])) !== JSON.stringify(argumentNames(ar[k])))
      .map((k) => `${k}: ${argumentNames(en[k])} vs ${argumentNames(ar[k])}`);
    expect(mismatched).toEqual([]);
  });

  it("Arabic texts contain Arabic script (not left in English)", () => {
    const untranslated = Object.entries(ar)
      .filter(([, v]) => /[A-Za-z]{4,}/.test(v.replace(/\{[^}]*\}/g, "")) && !/[؀-ۿ]/.test(v))
      .map(([k]) => k);
    // The brand name and the English language label stay Latin; anything else is a missed translation.
    const allowed = new Set(["common.brand", "common.languageNames.en", "auction.card.platform"]);
    expect(untranslated.filter((k) => !allowed.has(k))).toEqual([]);
  });
});
