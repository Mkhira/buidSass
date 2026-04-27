/**
 * T032i: POST /api/auth/step-up/start
 *
 * Used by spec 018 refunds + spec 019 account actions via `<StepUpDialog>`
 * (T040c). Starts a fresh step-up MFA challenge. The challenge id has a
 * short TTL (default 5 min per spec 004).
 */
import { NextResponse } from "next/server";
import { identityApi } from "@/lib/api/clients/identity";
import { getSession } from "@/lib/auth/session";
import { isApiError } from "@/lib/api/error";

export async function POST() {
  const session = await getSession();
  if (!session) {
    return NextResponse.json({ reasonCode: "auth.no_session" }, { status: 401 });
  }
  if (!session.mfaEnrolled) {
    return NextResponse.json({ reasonCode: "auth.step_up.no_factor_enrolled" }, { status: 412 });
  }
  try {
    const result = await identityApi.stepUp.start();
    return NextResponse.json(result);
  } catch (err) {
    if (isApiError(err)) {
      return NextResponse.json({ reasonCode: err.reasonCode ?? "auth.step_up.start_failed" }, { status: err.status });
    }
    return NextResponse.json({ reasonCode: "auth.step_up.start_failed" }, { status: 500 });
  }
}
