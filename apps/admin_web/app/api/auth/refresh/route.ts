/**
 * T024: POST /api/auth/refresh
 *
 * Silent — called by external clients that hold the sealed cookie and
 * need a freshly-rotated access token. Internal callers from inside the
 * Server Component / route-handler runtime should call
 * `refreshSessionInProcess()` directly so Set-Cookie propagation
 * reaches the calling response context.
 */
import { NextResponse } from "next/server";
import { refreshSessionInProcess } from "@/lib/auth/refresh";

export async function POST() {
  const refreshed = await refreshSessionInProcess();
  if (!refreshed) {
    return NextResponse.json(
      { kind: "error", reasonCode: "auth.refresh.failed" },
      { status: 401 },
    );
  }
  return NextResponse.json({ kind: "ok" });
}
