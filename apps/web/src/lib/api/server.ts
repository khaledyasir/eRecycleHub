import "server-only";
import createClient from "openapi-fetch";
import { cookies, headers } from "next/headers";
import { notFound } from "next/navigation";
import { getLocale } from "next-intl/server";
import { cache } from "react";
import { redirect } from "@/i18n/navigation";
import { toApiError, type paths, type SessionUser } from "./types";

const API = process.env.API_INTERNAL_URL ?? "http://localhost:5080";

/**
 * API client for server components: calls the API directly (internal URL) and forwards the visitor's cookies,
 * language and IP, so the API applies exactly the same authorisation as for browser calls. Never cached.
 */
export const serverApi = cache(async () => {
  const [c, h, locale] = await Promise.all([cookies(), headers(), getLocale()]);
  const forwarded = h.get("x-forwarded-for");
  return createClient<paths>({
    baseUrl: API,
    headers: {
      cookie: c.toString(),
      "x-locale": locale,
      ...(forwarded ? { "x-forwarded-for": forwarded } : {}),
    },
    fetch: (req) => fetch(req, { cache: "no-store" }),
  });
});

/** A session that expired mid-visit (layouts don't re-run on client navigation) goes to sign-in, not the error page. */
async function failed(res: Response): Promise<never> {
  if (res.status === 401) return redirect({ href: "/login", locale: await getLocale() });
  throw await toApiError(res);
}

/** Returns the data; 404 renders the not-found page, 401 goes to sign-in, other failures throw to the nearest error boundary. */
export async function load<T>(request: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const r = await request;
  if (r.response.status === 404) notFound();
  if (!r.response.ok) return failed(r.response);
  return r.data as T;
}

/** Same as load() but returns null instead of rendering 404 (for optional content). */
export async function loadOptional<T>(request: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T | null> {
  const r = await request;
  if (r.response.status === 404 || r.response.status === 204) return null;
  if (!r.response.ok) return failed(r.response);
  return (r.data as T) ?? null;
}

/** The signed-in user for this request (deduplicated per request), or null. */
export const getSession = cache(async (): Promise<SessionUser | null> => {
  const client = await serverApi();
  const r = await client.GET("/api/auth/me");
  return r.data?.user ?? null;
});

/** The visitor's selected country (0 / missing = all countries). */
export async function getCountryId(): Promise<number | undefined> {
  const v = Number((await cookies()).get("erh_country")?.value);
  return Number.isInteger(v) && v > 0 ? v : undefined;
}
