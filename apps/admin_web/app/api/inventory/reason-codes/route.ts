/**
 * T027 — reason-codes GET proxy. Cached privately for 5 minutes.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { proxyFetch } from "@/lib/api/proxy";

export async function GET() {
  const session = await getSession();
  if (!hasPermission(session, "inventory.read")) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }
  try {
    const result = await proxyFetch("/v1/admin/inventory/reason-codes");
    return NextResponse.json(result, {
      headers: { "Cache-Control": "private, max-age=300" },
    });
  } catch (err) {
    const message = err instanceof Error ? err.message : "unknown";
    return NextResponse.json({ error: message }, { status: 502 });
  }
}
