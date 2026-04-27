/**
 * T022: POST /api/auth/login
 *
 * Browser → Next.js → spec 004 sign-in. Spec 004 returns either a full
 * session payload or `mfa_required` with a partial-auth-token; we seal
 * the appropriate shape into the cookie either way (the partial state
 * lives under a temporary cookie if needed, but for v1 we keep the
 * client driving via the `kind: 'mfa_required'` response).
 */
import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { identityApi } from "@/lib/api/clients/identity";
import { writeSession, type AdminSessionPayload } from "@/lib/auth/session";
import { isApiError } from "@/lib/api/error";

const Body = z.object({
  email: z.string().email().max(254),
  password: z.string().min(1).max(256),
});

export async function POST(req: NextRequest) {
  let parsed;
  try {
    parsed = Body.parse(await req.json());
  } catch (err) {
    return NextResponse.json({ kind: "error", reasonCode: "invalid_request" }, { status: 400 });
  }

  try {
    const result = await identityApi.signIn(parsed);
    if (result.kind === "ok") {
      await writeSession(result.session as AdminSessionPayload);
      return NextResponse.json({ kind: "ok" });
    }
    if (result.kind === "mfa_required") {
      return NextResponse.json({
        kind: "mfa_required",
        partialAuthToken: result.partialAuthToken,
        channel: result.channel,
      });
    }
    return NextResponse.json(result, { status: 400 });
  } catch (err) {
    if (isApiError(err)) {
      return NextResponse.json(
        { kind: "error", reasonCode: err.reasonCode ?? "auth.signin.failed" },
        { status: err.status },
      );
    }
    return NextResponse.json({ kind: "error", reasonCode: "auth.signin.failed" }, { status: 500 });
  }
}
