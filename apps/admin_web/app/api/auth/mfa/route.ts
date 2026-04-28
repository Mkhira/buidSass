/**
 * T023: POST /api/auth/mfa
 */
import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { identityApi } from "@/lib/api/clients/identity";
import { writeSession, type AdminSessionPayload } from "@/lib/auth/session";
import { isApiError } from "@/lib/api/error";

const Body = z.object({
  partialAuthToken: z.string().min(1),
  code: z.string().regex(/^\d{6}$/),
});

export async function POST(req: NextRequest) {
  let parsed;
  try {
    parsed = Body.parse(await req.json());
  } catch (err) {
    return NextResponse.json({ kind: "error", reasonCode: "invalid_request" }, { status: 400 });
  }

  try {
    const result = await identityApi.mfa(parsed);
    if (result.kind === "ok") {
      await writeSession(result.session as AdminSessionPayload);
      return NextResponse.json({ kind: "ok" });
    }
    return NextResponse.json(result, { status: 400 });
  } catch (err) {
    if (isApiError(err)) {
      return NextResponse.json(
        { kind: "error", reasonCode: err.reasonCode ?? "auth.mfa.failed" },
        { status: err.status },
      );
    }
    return NextResponse.json({ kind: "error", reasonCode: "auth.mfa.failed" }, { status: 500 });
  }
}
