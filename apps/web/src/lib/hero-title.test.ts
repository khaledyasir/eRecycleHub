import { describe, expect, it } from "vitest";
import { parseHeroTitle } from "./hero-title";

describe("parseHeroTitle", () => {
  it("colours {green} and [purple] parts and splits lines", () => {
    expect(parseHeroTitle("Your chance starts {here}.\nBid, win, [and recycle]")).toEqual([
      [{ text: "Your chance starts ", tone: "plain" }, { text: "here", tone: "green" }, { text: ".", tone: "plain" }],
      [{ text: "Bid, win, ", tone: "plain" }, { text: "and recycle", tone: "purple" }],
    ]);
  });

  it("keeps unmatched brackets and drops blank lines", () => {
    expect(parseHeroTitle("Price {from 10\r\n\r\n[x] <b>y</b>")).toEqual([
      [{ text: "Price {from 10", tone: "plain" }],
      [{ text: "x", tone: "purple" }, { text: " <b>y</b>", tone: "plain" }],
    ]);
  });

  it("returns nothing for empty text", () => {
    expect(parseHeroTitle("  \n ")).toEqual([]);
  });
});
