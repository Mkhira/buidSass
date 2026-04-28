/**
 * T032i: POST /api/auth/step-up/complete
 *
 * Verifies the TOTP / push code against spec 004's challenge and returns
 * the assertion id the calling form forwards as `X-StepUp-Assertion`.
 */
import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { identityApi } from "@/lib/api/clients/identity";
import { getSession } from "@/lib/auth/session";
import { isApiError } from "@/lib/api/error";

const Body = z.object({
  challengeId: z.string().min(1),
  code: z.string().regex(/^\d{6}$/),
});

export async function POST(req: NextRequest) {
  const session = await getSession();
  if (!session) {
    return NextResponse.json({ reasonCode: "auth.no_session" }, { status: 401 });
  }
  let parsed;
  try {
    parsed = Body.parse(await req.json());
  } catch {
    return NextResponse.json({ reasonCode: "invalid_request" }, { status: 400 });
  }
  try {
    const result = await identityApi.stepUp.complete(parsed.challengeId, parsed.code);
    return NextResponse.json(result);
  } catch (err) {
    if (isApiError(err)) {
      return NextResponse.json(
        { reasonCode: err.reasonCode ?? "auth.step_up.complete_failed" },
        { status: err.status },
      );
    }
    return NextResponse.json({ reasonCode: "auth.step_up.complete_failed" }, { status: 500 });
  }
}
