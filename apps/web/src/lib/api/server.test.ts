// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "./types";

vi.mock("next/headers", () => ({ cookies: vi.fn(), headers: vi.fn() }));
vi.mock("next-intl/server", () => ({ getLocale: vi.fn() }));
vi.mock("@/i18n/navigation", () => ({ redirect: vi.fn() }));

import { loadPublicOptional } from "./server";

const result = (status: number, data?: string) => Promise.resolve({ data, response: new Response(null, { status }) });

describe("public optional content", () => {
  it("returns content and accepts absent content", async () => {
    await expect(loadPublicOptional(result(200, "content"))).resolves.toBe("content");
    await expect(loadPublicOptional(result(404))).resolves.toBeNull();
    await expect(loadPublicOptional(result(204))).resolves.toBeNull();
  });

  it.each([401, 403, 500])("surfaces HTTP %i without redirecting to login", async (status) => {
    await expect(loadPublicOptional(result(status))).rejects.toMatchObject({ status });
    await expect(loadPublicOptional(result(status))).rejects.toBeInstanceOf(ApiError);
  });
});
