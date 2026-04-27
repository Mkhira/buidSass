/**
 * T024: POST /api/auth/refresh
 *
 * Silent — called by `lib/api/proxy.ts` when a 401 lands on a request
 * the session said should still be valid. Reads the refresh token from
 * the sealed cookie, calls spec 004, re-seals on success.
 */
import { NextResponse } from "next/server";
import { identityApi } from "@/lib/api/clients/identity";
import { getSession, writeSession, clearSession, type AdminSessionPayload } from "@/lib/auth/session";

export async function POST() {
  const session = await getSession();
  if (!session) {
    clearSession();
    return NextResponse.json({ kind: "error", reasonCode: "auth.refresh.no_session" }, { status: 401 });
  }
  try {
    const refreshed = await identityApi.refresh(session.refreshToken);
    await writeSession(refreshed as AdminSessionPayload);
    return NextResponse.json({ kind: "ok" });
  } catch {
    clearSession();
    return NextResponse.json({ kind: "error", reasonCode: "auth.refresh.failed" }, { status: 401 });
  }
}
