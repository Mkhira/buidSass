/**
 * T015 + T035: (admin) layout — mounts AppShell after session + permission gate.
 *
 * Also wraps children in `<SessionProvider>` so descendant Client
 * Components can call `useSession()` / `usePermission()` for
 * permission-gated UI without re-fetching.
 */
import { headers } from "next/headers";
import type { ReactNode } from "react";
import { permissionsForRoute } from "@/lib/auth/permissions";
import { requirePermission, requireSession } from "@/lib/auth/guards";
import { AppShell } from "@/components/shell/app-shell";
import {
  SessionProvider,
  type ClientSessionPayload,
} from "@/components/providers/session-provider";

export default async function AdminLayout({ children }: { children: ReactNode }) {
  const hdrs = headers();
  const pathname = hdrs.get("x-pathname") ?? hdrs.get("x-invoke-path") ?? "/";
  const required = permissionsForRoute(pathname);

  const session = required && required.length > 0
    ? await requirePermission(required, pathname)
    : await requireSession(pathname);

  // Strip tokens server-side — RSC serialises every prop, and the
  // accessToken / refreshToken / expiresAt fields on AdminSessionPayload
  // must never cross the boundary to a Client Component.
  const clientSession: ClientSessionPayload = {
    adminId: session.adminId,
    email: session.email,
    displayName: session.displayName,
    roleScope: session.roleScope,
    roles: session.roles,
    permissions: session.permissions,
    mfaEnrolled: session.mfaEnrolled,
  };

  return (
    <SessionProvider session={clientSession}>
      <AppShell session={session}>{children}</AppShell>
    </SessionProvider>
  );
}
