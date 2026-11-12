import { describe, expect, it } from "vitest";
import { sparkline } from "./price-chart";

describe("sparkline", () => {
  it("needs at least two points", () => {
    expect(sparkline([])).toBeNull();
    expect(sparkline([5])).toBeNull();
  });

  it("spreads points left to right, lowest at the bottom and highest at the top", () => {
    const s = sparkline([100, 150, 200], 100, 50, 10)!;
    expect(s.line).toBe("M10,40 C30,40 30,25 50,25 C70,25 70,10 90,10");
    expect(s.end).toEqual({ x: 90, y: 10 });
    expect(s.area).toBe(`${s.line} L90,50 L10,50 Z`);
  });

  it("draws a flat line when the price never moved", () => {
    expect(sparkline([7, 7], 100, 50, 10)!.line).toBe("M10,40 C50,40 50,40 90,40");
  });
});
