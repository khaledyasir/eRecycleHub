import type { components, paths } from "./schema";

/** Shorthand for API schema types: `Schema<"AuctionCard">`. Generated from the API's OpenAPI document (npm run api:types). */
export type Schema<K extends keyof components["schemas"]> = components["schemas"][K];
export type { paths };

export type AuctionCard = Schema<"AuctionCard">;
export type AuctionDetails = Schema<"AuctionDetails">;
export type AuctionPhase = Schema<"AuctionPhase">;
export type SessionUser = Schema<"SessionUserDto">;
export type UserRole = Schema<"UserRole">;

export type Paged<T> = { items: T[]; page: number; pageSize: number; total: number; totalPages?: number };

/** Problem details as returned by the API: `code` is stable and maps to a message in messages/<locale>/errors.json. */
export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    public args?: Record<string, unknown>,
  ) {
    super(code);
  }
}

export async function toApiError(res: Response): Promise<ApiError> {
  try {
    const body = await res.json();
    return new ApiError(res.status, body?.code ?? body?.title ?? `http.${res.status}`, body?.args ?? undefined);
  } catch {
    return new ApiError(res.status, `http.${res.status}`);
  }
}
