import { describe, expect, it } from "vitest";
import { safeNext } from "./auth-forms";

describe("login return destinations", () => {
  it.each(["/account", "/seller?tab=lots"])("allows %s", (path) => expect(safeNext(path)).toBe(path));
  it.each(["https://evil.example", "//evil.example", "/\\evil.example", "/\n/evil.example", "/login", "/ar/login?next=/login", "/en/login/", "/a/../login"])("rejects %s", (path) => expect(safeNext(path)).toBeNull());
});
