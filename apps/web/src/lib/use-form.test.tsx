import { act, renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api/types";
import { Intl } from "@/test/intl";
import { passwordCriteria, useForm, useValidators } from "./use-form";

const wrapper = ({ children }: { children: ReactNode }) => <Intl>{children}</Intl>;

describe("useValidators", () => {
  const v = () => renderHook(() => useValidators(), { wrapper }).result.current;

  it("accepts money with at most two decimals and rejects the rest", () => {
    const { amount } = v();
    for (const ok of ["1", "10.5", "10.50", "0.01", "1000000"]) expect(amount(ok), ok).toBeUndefined();
    for (const bad of ["", "abc", "1.234", "-5", "1,5", "1e3", " "]) expect(amount(bad), bad).toBeTruthy();
    expect(amount("0")).toBeTruthy(); // positive by default
    expect(amount("0", { positive: false })).toBeUndefined();
  });

  it("checks email, length, matching passwords and links", () => {
    const x = v();
    expect(x.email("a@b.co")).toBeUndefined();
    expect(x.email("a@b")).toBeTruthy();
    expect(x.min("1234567", 8)).toBeTruthy();
    expect(x.min("12345678", 8)).toBeUndefined();
    expect(x.match("a", "b")).toBeTruthy();
    expect(x.url("")).toBeUndefined(); // optional
    expect(x.url("https://example.com")).toBeUndefined();
    expect(x.url("javascript:alert(1)")).toBeTruthy();
  });
});

describe("strongPassword", () => {
  it("needs 8+ characters with upper case, lower case, a digit and a symbol, and no spaces", () => {
    const { strongPassword } = renderHook(() => useValidators(), { wrapper }).result.current;
    for (const ok of ["TestPass#2026", "aB3$efgh", "Pass_word1"]) expect(strongPassword(ok), ok).toBeUndefined();
    for (const bad of ["", "aB3$efg", "ab3$efgh", "AB3$EFGH", "aBc$efgh", "aB3defgh", "aB3$ efgh", "aB3 efgh"]) expect(strongPassword(bad), bad).toBeTruthy();
    expect(passwordCriteria("abc")).toEqual({ length: false, case: false, number: false, symbol: false });
    expect(passwordCriteria("Abcdefg1")).toEqual({ length: true, case: true, number: true, symbol: false });
  });
});

describe("useForm", () => {
  it("does not run the action while validation fails, and clears a field's error when it changes", async () => {
    const action = vi.fn();
    const { result } = renderHook(() => useForm({ name: "" }), { wrapper });
    await act(async () => result.current.submit((x) => ({ name: x.name ? undefined : "required" }), action)({ preventDefault() {} } as never));
    expect(action).not.toHaveBeenCalled();
    expect(result.current.errors.name).toBe("required");
    act(() => result.current.set("name", "x"));
    expect(result.current.errors.name).toBeUndefined();
  });

  it("keeps the entered values and shows the localised API error when the action fails", async () => {
    const { result } = renderHook(() => useForm({ name: "keep me" }), { wrapper });
    await act(async () =>
      result.current.submit(() => ({}), async () => { throw new ApiError(409, "auth.email_taken"); })({ preventDefault() {} } as never),
    );
    expect(result.current.values.name).toBe("keep me");
    expect(result.current.formError).toBeTruthy();
    expect(result.current.formError).not.toBe("auth.email_taken"); // a sentence, not the code
    expect(result.current.busy).toBe(false);
  });
});
