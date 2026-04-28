/**
 * T025: POST /api/auth/logout
 */
import { NextResponse } from "next/server";
import { identityApi } from "@/lib/api/clients/identity";
import { getSession, clearSession } from "@/lib/auth/session";

export async function POST() {
  const session = await getSession();
  // Best-effort revoke; clear cookie regardless of upstream success.
  if (session) {
    try {
      await identityApi.revoke(session.adminId);
    } catch {
      // swallow — we still want to clear the local cookie
    }
  }
  clearSession();
  return NextResponse.json({ kind: "ok" });
}
