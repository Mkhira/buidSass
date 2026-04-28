/**
 * T021: Single fetch wrapper used by every Next.js Route Handler that
 * proxies the .NET backend.
 *
 * Responsibilities:
 *  - Read the iron-sealed session cookie via `getSession()`.
 *  - Attach `Authorization: Bearer <accessToken>`.
 *  - Attach `X-Correlation-Id` (UUID v4 per request).
 *  - Attach `Accept-Language` from the active locale + market.
 *  - Attach `X-Market-Code` from the session's role scope.
 *  - On a 401 (token expired), call `/api/auth/refresh` once and retry.
 *  - Map non-2xx responses to ApiError.
 *
 * NOTE: this module runs on the server only — never imported by Client
 * Components. The `lib/api/clients/*` thin wrappers consume it.
 */
import { randomUUID } from "node:crypto";
import { ApiError } from "./error";
import { bcp47For } from "@/lib/i18n/config";
import { resolveLocale } from "@/lib/i18n/server";
import { getSession, type AdminSessionPayload } from "@/lib/auth/session";
import { refreshSessionInProcess } from "@/lib/auth/refresh";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5000";

export interface ProxyOptions extends Omit<RequestInit, "headers"> {
  headers?: Record<string, string>;
  /** Skip auth header (used by the auth-proxy itself). */
  unauthenticated?: boolean;
  /** Idempotency key for mutating requests (FR-013 / 013). */
  idempotencyKey?: string;
  /** Step-up assertion id when calling step-up-gated endpoints. */
  stepUpAssertion?: string;
}

export async function proxyFetch<T>(path: string, opts: ProxyOptions = {}): Promise<T> {
  const url = path.startsWith("http") ? path : `${BACKEND_URL}${path}`;
  const session = opts.unauthenticated ? null : await getSession();

  const localeHeader = buildAcceptLanguage(session);
  const marketHeader = session?.roleScope ?? "platform";

  const reqHeaders: Record<string, string> = {
    "Content-Type": "application/json",
    "Accept-Language": localeHeader,
    "X-Market-Code": marketHeader,
    "X-Correlation-Id": randomUUID(),
    ...opts.headers,
  };

  if (session && !opts.unauthenticated) {
    reqHeaders["Authorization"] = `Bearer ${session.accessToken}`;
  }
  if (opts.idempotencyKey) reqHeaders["Idempotency-Key"] = opts.idempotencyKey;
  if (opts.stepUpAssertion) reqHeaders["X-StepUp-Assertion"] = opts.stepUpAssertion;

  const response = await fetch(url, { ...opts, headers: reqHeaders, cache: "no-store" });

  if (response.status === 401 && session && !opts.unauthenticated) {
    // Refresh-once-and-retry path. The actual refresh endpoint
    // (T024) writes a fresh session cookie; we re-read it here.
    const refreshed = await refreshOnce();
    if (refreshed) {
      reqHeaders["Authorization"] = `Bearer ${refreshed.accessToken}`;
      const retry = await fetch(url, { ...opts, headers: reqHeaders, cache: "no-store" });
      if (!retry.ok) throw await ApiError.fromResponse(retry);
      return parseBody<T>(retry);
    }
  }

  if (!response.ok) throw await ApiError.fromResponse(response);
  return parseBody<T>(response);
}

async function parseBody<T>(res: Response): Promise<T> {
  if (res.status === 204) return undefined as T;
  const ct = res.headers.get("content-type") ?? "";
  if (ct.includes("application/json")) return (await res.json()) as T;
  return (await res.text()) as unknown as T;
}

function buildAcceptLanguage(session: AdminSessionPayload | null): string {
  const locale = resolveLocale();
  const market = session?.roleScope ?? "platform";
  return bcp47For(locale, market);
}

/**
 * Internal — refreshes the access token in-process via the shared
 * `refreshSessionInProcess()` helper. Self-fetching the refresh route
 * handler does not work in this code path because Set-Cookie headers
 * from a subrequest do not propagate back to the parent request's
 * cookie jar.
 */
async function refreshOnce(): Promise<AdminSessionPayload | null> {
  return refreshSessionInProcess();
}
