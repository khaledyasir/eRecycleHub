import { describe, expect, it } from "vitest";
import { dateTime, fileUrl, fromLocalInput, money, number, pick, relativeTime, toLocalInput } from "./format";

describe("money", () => {
  it("always shows 2 decimals, the currency and Latin digits, also in Arabic", () => {
    expect(money(1234.5, "en")).toMatch(/1,234\.50/);
    expect(money(1234.5, "en")).toContain("JOD");
    const ar = money(1234.5, "ar");
    expect(ar).toMatch(/1[,٬]234[.٫]50/);
    expect(ar).not.toMatch(/[٠-٩]/);
  });
  it("does not round half-cent display of exact values", () => {
    expect(money(0.1 + 0.2, "en")).toMatch(/0\.30/);
  });
});

describe("number", () => {
  it("uses Latin digits in Arabic", () => {
    expect(number(12345, "ar")).not.toMatch(/[٠-٩]/);
  });
});

describe("date and time", () => {
  it("renders in the display time zone (Asia/Amman, UTC+3 in summer), not the viewer's", () => {
    // 21:30 UTC on 14 Oct is 00:30 on 15 Oct in Amman (UTC+3 after the 2022 permanent-summer-time change).
    expect(toLocalInput("2026-10-14T21:30:00Z")).toBe("2026-10-15T00:30");
    expect(dateTime("2026-10-14T21:30:00Z", "en", false)).toContain("15");
  });
  it("converts a wall-clock input back to the same UTC instant (round trip)", () => {
    for (const iso of ["2026-01-05T08:00:00.000Z", "2026-07-20T23:59:00.000Z", "2026-03-29T00:30:00.000Z"]) {
      expect(fromLocalInput(toLocalInput(iso))).toBe(iso);
    }
  });
  it("treats the input as display-zone time", () => {
    expect(fromLocalInput("2026-10-15T00:30")).toBe("2026-10-14T21:30:00.000Z");
  });
  it("returns empty for missing values", () => {
    expect(toLocalInput(null)).toBe("");
    expect(dateTime(null, "en")).toBeDefined();
  });
  it("describes relative time without throwing for past and future", () => {
    const now = Date.parse("2026-10-14T12:00:00Z");
    expect(relativeTime("2026-10-14T11:00:00Z", "en", now)).toBeTruthy();
    expect(relativeTime("2026-10-15T12:00:00Z", "ar", now)).toBeTruthy();
  });
});

describe("pick", () => {
  it("prefers the UI language and falls back to the other", () => {
    expect(pick("en", "Cars", "سيارات")).toBe("Cars");
    expect(pick("ar", "Cars", "سيارات")).toBe("سيارات");
    expect(pick("ar", "Cars", "")).toBe("Cars");
    expect(pick("en", null, "سيارات")).toBe("سيارات");
    expect(pick("en", null, null)).toBe("");
  });
});

describe("fileUrl", () => {
  it("builds same-origin file URLs and tolerates missing ids", () => {
    expect(fileUrl("abc")).toBe("/api/files/abc");
    expect(fileUrl(null)).toBeUndefined();
  });
});
