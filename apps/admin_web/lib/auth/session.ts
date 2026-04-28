/**
 * T020: iron-session config — sealed cookie carrying tokens + identity.
 *
 * T032b (FR-028d): dual-secret rotation window. The server reads
 * `IRON_SESSION_PASSWORD` (current) and `IRON_SESSION_PASSWORD_PREV`
 * (previous, optional). Decrypt with current first; on failure, fall back
 * to previous. Successful previous-secret reads trigger an immediate
 * re-seal under the current secret on the next response (see
 * `getSession()` consumers).
 *
 * Cookie shape (sealed):
 *   { adminId, email, displayName, roleScope, roles, permissions,
 *     accessToken, refreshToken, expiresAt, mfaEnrolled }
 *
 * Cookie attributes: httpOnly, Secure, SameSite=Strict, path=/.
 */
import { cookies as nextCookies } from "next/headers";
import { sealData, unsealData, type SessionOptions } from "iron-session";

export interface AdminSessionPayload {
  adminId: string;
  email: string;
  displayName: string;
  roleScope: "platform" | "ksa" | "eg";
  roles: string[];
  permissions: string[];
  accessToken: string;
  refreshToken: string;
  expiresAt: number; // epoch seconds
  mfaEnrolled: boolean;
}

export const SESSION_COOKIE = "admin_session";

const COOKIE_OPTIONS = {
  httpOnly: true,
  secure: process.env.NODE_ENV === "production",
  sameSite: "strict" as const,
  path: "/",
  maxAge: 60 * 60 * 24 * 30, // 30 days
};

function currentSecret(): string {
  const v = process.env.IRON_SESSION_PASSWORD;
  if (!v || v.length < 32) {
    throw new Error(
      "IRON_SESSION_PASSWORD must be set and ≥ 32 chars. See README.md Operations.",
    );
  }
  return v;
}

function previousSecret(): string | undefined {
  const v = process.env.IRON_SESSION_PASSWORD_PREV;
  return v && v.length >= 32 ? v : undefined;
}

function ironOptions(secret: string): SessionOptions {
  return {
    cookieName: SESSION_COOKIE,
    password: secret,
    cookieOptions: COOKIE_OPTIONS,
  };
}

export interface SealOutcome {
  payload: AdminSessionPayload | null;
  /** true iff cookie was decrypted with the previous secret — caller MUST re-seal. */
  needsReseal: boolean;
}

/**
 * Read the sealed cookie and unseal it. Tries current secret first, falls
 * back to previous (T032b). Returns `{ payload: null }` if no cookie or
 * neither secret can decrypt it.
 */
export async function readSession(rawCookie?: string): Promise<SealOutcome> {
  const cookieJar = rawCookie === undefined ? nextCookies() : null;
  const sealed = rawCookie ?? cookieJar?.get(SESSION_COOKIE)?.value;
  if (!sealed) return { payload: null, needsReseal: false };

  // Try current secret
  try {
    const payload = await unsealData<AdminSessionPayload>(sealed, ironOptions(currentSecret()));
    if (payload && payload.adminId) return { payload, needsReseal: false };
  } catch {
    // fall through
  }

  // Fall back to previous secret if present
  const prev = previousSecret();
  if (prev) {
    try {
      const payload = await unsealData<AdminSessionPayload>(sealed, ironOptions(prev));
      if (payload && payload.adminId) return { payload, needsReseal: true };
    } catch {
      // fall through
    }
  }

  return { payload: null, needsReseal: false };
}

export async function writeSession(payload: AdminSessionPayload): Promise<void> {
  const sealed = await sealData(payload, ironOptions(currentSecret()));
  nextCookies().set(SESSION_COOKIE, sealed, COOKIE_OPTIONS);
}

export function clearSession(): void {
  nextCookies().set(SESSION_COOKIE, "", { ...COOKIE_OPTIONS, maxAge: 0 });
}

/** Convenience for Server Components that just need the unsealed payload. */
export async function getSession(): Promise<AdminSessionPayload | null> {
  const { payload, needsReseal } = await readSession();
  if (payload && needsReseal) {
    // Re-seal under the current secret transparently.
    await writeSession(payload);
  }
  return payload;
}
