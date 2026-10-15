export type HeroTone = "plain" | "green" | "purple";
export type HeroSegment = { text: string; tone: HeroTone };

const MARK = /\{([^{}\n]+)\}|\[([^[\]\n]+)\]/g;

/**
 * Splits an admin-edited headline into lines of coloured segments: `{text}` → green, `[text]` → purple, a line break → a new line.
 * Output is plain text only (rendered by React, never as HTML); unmatched brackets are kept as typed.
 */
export function parseHeroTitle(source: string): HeroSegment[][] {
  return source
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const out: HeroSegment[] = [];
      let last = 0;
      for (const m of line.matchAll(MARK)) {
        if (m.index > last) out.push({ text: line.slice(last, m.index), tone: "plain" });
        out.push(m[1] !== undefined ? { text: m[1], tone: "green" } : { text: m[2], tone: "purple" });
        last = m.index + m[0].length;
      }
      if (last < line.length) out.push({ text: line.slice(last), tone: "plain" });
      return out;
    });
}
