import createClient, { type Middleware } from "openapi-fetch";
import { ApiError, toApiError, type paths } from "./types";

/**
 * Browser API client. Same origin (/api is proxied to ASP.NET Core), cookie session, and the antiforgery
 * token sent as X-CSRF-TOKEN on every unsafe request. Tokens are bound to the signed-in identity, so callers
 * that change who is signed in pass the token returned by the API to setCsrfToken().
 */
let csrfToken: string | null = null;
let pending: Promise<string> | null = null;

export function setCsrfToken(token: string | null | undefined) {
  csrfToken = token ?? null;
}

async function ensureCsrf(): Promise<string> {
  if (csrfToken) return csrfToken;
  pending ??= fetch("/api/auth/me", { credentials: "same-origin", cache: "no-store" })
    .then((r) => r.json())
    .then((b: { csrfToken: string }) => (csrfToken = b.csrfToken))
    .finally(() => (pending = null));
  return pending;
}

const locale = () => (typeof document === "undefined" ? "en" : document.documentElement.lang || "en");
const isUnsafe = (method: string) => !["GET", "HEAD", "OPTIONS"].includes(method.toUpperCase());

const headersMiddleware: Middleware = {
  async onRequest({ request }) {
    request.headers.set("X-Locale", locale());
    if (isUnsafe(request.method)) request.headers.set("X-CSRF-TOKEN", await ensureCsrf());
    return request;
  },
};

export const api = createClient<paths>({ baseUrl: "", credentials: "same-origin" });
api.use(headersMiddleware);

type Result<T> = { data?: T; error?: unknown; response: Response };

/**
 * Unwraps an openapi-fetch call: returns data or throws ApiError. A rejected antiforgery token (expired, or the
 * session changed in another tab) is refreshed once and the request retried.
 */
export async function call<T>(request: () => Promise<Result<T>>): Promise<T> {
  let r = await request();
  if (!r.response.ok) {
    const err = await toApiError(r.response.clone());
    if (err.code !== "csrf.invalid") throw err;
    csrfToken = null;
    r = await request();
    if (!r.response.ok) throw await toApiError(r.response);
  }
  return r.data as T;
}

/**
 * Multipart request following the API convention: JSON fields in "data", files as named parts
 * (a File[] value sends several parts with the same name).
 */
export async function sendForm<T = unknown>(method: "POST" | "PUT", path: string, data: unknown, files: Record<string, File | File[] | null | undefined> = {}): Promise<T> {
  const build = async () => {
    const form = new FormData();
    form.append("data", JSON.stringify(data));
    for (const [name, value] of Object.entries(files)) {
      for (const f of Array.isArray(value) ? value : value ? [value] : []) form.append(name, f, f.name);
    }
    return fetch(path, { method, body: form, credentials: "same-origin", headers: { "X-CSRF-TOKEN": await ensureCsrf(), "X-Locale": locale() } });
  };
  let res = await build();
  if (!res.ok) {
    const err = await toApiError(res.clone());
    if (err.code !== "csrf.invalid") throw err;
    csrfToken = null;
    res = await build();
    if (!res.ok) throw await toApiError(res);
  }
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** JSON request to a path built at runtime (for code shared between /api/seller and /api/admin). Same CSRF + retry handling. */
export async function sendJson<T = unknown>(method: "POST" | "PUT" | "DELETE", path: string, body?: unknown): Promise<T> {
  const build = async () =>
    fetch(path, {
      method,
      credentials: "same-origin",
      headers: { "X-CSRF-TOKEN": await ensureCsrf(), "X-Locale": locale(), ...(body === undefined ? {} : { "Content-Type": "application/json" }) },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  let res = await build();
  if (!res.ok) {
    const err = await toApiError(res.clone());
    if (err.code !== "csrf.invalid") throw err;
    csrfToken = null;
    res = await build();
    if (!res.ok) throw await toApiError(res);
  }
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export { ApiError };
