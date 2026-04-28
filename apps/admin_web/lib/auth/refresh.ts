/**
 * Shared refresh logic.
 *
 * Called both by `lib/api/proxy.ts` (in-process, on 401) and by the
 * `/api/auth/refresh` route handler. Avoids a self-fetch loopback
 * subrequest, which has two problems:
 *   1. Set-Cookie headers from a subrequest don't propagate back to
 *      the parent request's cookie jar — the refreshed session is
 *      never visible to the caller that triggered the refresh.
 *   2. Constructing the loopback URL from untrusted `Host` /
 *      `X-Forwarded-Proto` request headers is a host-header poisoning
 *      vector when the sealed session cookie is being forwarded.
 *
 * This helper reads the cookie via `getSession()`, calls the spec 004
 * backend over `BACKEND_URL`, and writes the new sealed cookie to the
 * caller's response context via `writeSession()`.
 */
import { identityApi } from "@/lib/api/clients/identity";
import {
  clearSession,
  getSession,
  writeSession,
  type AdminSessionPayload,
} from "@/lib/auth/session";

/**
 * Refresh the access token in-process. Returns the new
 * `AdminSessionPayload` on success, `null` on any failure (in which
 * case the sealed cookie has been cleared).
 */
export async function refreshSessionInProcess(): Promise<
  AdminSessionPayload | null
> {
  const session = await getSession();
  if (!session) {
    clearSession();
    return null;
  }
  try {
    const refreshed = (await identityApi.refresh(
      session.refreshToken,
    )) as AdminSessionPayload;
    await writeSession(refreshed);
    return refreshed;
  } catch {
    clearSession();
    return null;
  }
}
