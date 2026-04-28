/**
 * Client Component session provider — exposes the unsealed admin
 * session payload to descendant Client Components via React context.
 *
 * The (admin) layout reads the session server-side (deeply unsealing
 * the iron cookie) and passes the payload here. Downstream Client
 * Components consume `useSession()` to render permission-gated UI
 * (e.g., hiding action buttons the actor lacks permission for) without
 * re-fetching.
 *
 * This is intentionally session-payload-only — no tokens. The token
 * never leaves the server (Q1 / FR-007).
 */
"use client";

import { createContext, useContext, useMemo } from "react";
import type { ReactNode } from "react";

/**
 * Token-free DTO that crosses the server→client boundary.
 *
 * IMPORTANT: do NOT widen this to `AdminSessionPayload` — that type
 * carries `accessToken` / `refreshToken` / `expiresAt`, and React
 * Server Components serialise every prop to the browser before the
 * Client Component runs. The `useMemo` below cannot strip what's
 * already on the wire. Layouts must construct this DTO server-side.
 */
export interface ClientSessionPayload {
  adminId: string;
  email: string;
  displayName: string;
  roleScope: "platform" | "ksa" | "eg";
  roles: string[];
  permissions: string[];
  mfaEnrolled: boolean;
}

export interface ClientSession {
  adminId: string;
  email: string;
  displayName: string;
  roleScope: "platform" | "ksa" | "eg";
  roles: string[];
  permissions: ReadonlySet<string>;
  mfaEnrolled: boolean;
}

const SessionContext = createContext<ClientSession | null>(null);

interface SessionProviderProps {
  /** Token-free server-side projection — see `ClientSessionPayload`. */
  session: ClientSessionPayload;
  children: ReactNode;
}

export function SessionProvider({ session, children }: SessionProviderProps) {
  const value = useMemo<ClientSession>(
    () => ({
      adminId: session.adminId,
      email: session.email,
      displayName: session.displayName,
      roleScope: session.roleScope,
      roles: session.roles,
      permissions: new Set(session.permissions),
      mfaEnrolled: session.mfaEnrolled,
    }),
    [session],
  );
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

/**
 * Returns the active session payload. Throws if called outside the
 * provider — that's a developer error: every (admin)-group Client
 * Component is descended from `<SessionProvider>` mounted in the
 * layout.
 */
export function useSession(): ClientSession {
  const ctx = useContext(SessionContext);
  if (ctx === null) {
    throw new Error(
      "useSession() called outside <SessionProvider>. Mount the provider in app/(admin)/layout.tsx.",
    );
  }
  return ctx;
}

/**
 * Convenience: returns true iff the actor holds the given permission.
 * Accepts a single key or an array (logical AND).
 */
export function usePermission(permissions: string | string[]): boolean {
  const session = useSession();
  const keys = typeof permissions === "string" ? [permissions] : permissions;
  // Guard against `[]` — `Array.prototype.every` returns true on an
  // empty array, which would silently grant access. Require at least
  // one key; an empty list is treated as "no permission asserted".
  if (keys.length === 0) return false;
  return keys.every((k) => session.permissions.has(k));
}

/**
 * Convenience: returns true iff the actor holds at least one of the
 * given permissions (logical OR).
 */
export function useAnyPermission(permissions: string[]): boolean {
  const session = useSession();
  return permissions.some((k) => session.permissions.has(k));
}
