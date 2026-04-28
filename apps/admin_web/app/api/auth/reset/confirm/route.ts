/**
 * T052 (server): POST /api/auth/reset/confirm — proxies spec 004 reset
 * confirm. Returns kind: "ok" | "error".
 */
import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { proxyFetch } from "@/lib/api/proxy";
import { isApiError } from "@/lib/api/error";

const Body = z.object({
  token: z.string().min(1),
  password: z.string().min(12).max(256),
});

export async function POST(req: NextRequest) {
  let parsed;
  try {
    parsed = Body.parse(await req.json());
  } catch {
    return NextResponse.json({ kind: "error", reasonCode: "invalid_request" }, { status: 400 });
  }
  try {
    await proxyFetch<void>("/v1/admin/identity/reset/confirm", {
      method: "POST",
      unauthenticated: true,
      body: JSON.stringify(parsed),
    });
    return NextResponse.json({ kind: "ok" });
  } catch (err) {
    if (isApiError(err)) {
      return NextResponse.json(
        { kind: "error", reasonCode: err.reasonCode ?? "auth.reset.failed" },
        { status: err.status },
      );
    }
    return NextResponse.json({ kind: "error", reasonCode: "auth.reset.failed" }, { status: 500 });
  }
}
