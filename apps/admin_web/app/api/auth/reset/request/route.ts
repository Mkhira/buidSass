/**
 * T052 (server): POST /api/auth/reset/request — proxies spec 004 reset
 * request. Email enumeration protection: always returns 200 regardless
 * of whether the email exists.
 */
import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { proxyFetch } from "@/lib/api/proxy";

const Body = z.object({ email: z.string().email().max(254) });

export async function POST(req: NextRequest) {
  let parsed;
  try {
    parsed = Body.parse(await req.json());
  } catch {
    return NextResponse.json({ kind: "ok" }); // intentionally non-revealing
  }
  try {
    await proxyFetch<void>("/v1/admin/identity/reset/request", {
      method: "POST",
      unauthenticated: true,
      body: JSON.stringify(parsed),
    });
  } catch {
    // best-effort; never reveal failure to the caller
  }
  return NextResponse.json({ kind: "ok" });
}
