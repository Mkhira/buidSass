/**
 * T026 — adjustments POST proxy with idempotency-key forwarding.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { proxyFetch } from "@/lib/api/proxy";

export async function POST(request: Request) {
  const session = await getSession();
  if (!hasPermission(session, "inventory.adjust")) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }
  const body = await request.json().catch(() => null);
  if (!body) {
    return NextResponse.json({ error: "invalid_body" }, { status: 422 });
  }
  const idempotencyKey =
    request.headers.get("Idempotency-Key") ?? crypto.randomUUID();
  try {
    const result = await proxyFetch("/v1/admin/inventory/adjustments", {
      method: "POST",
      body: JSON.stringify(body),
      idempotencyKey,
    });
    return NextResponse.json(result);
  } catch (err) {
    const message = err instanceof Error ? err.message : "unknown";
    return NextResponse.json({ error: message }, { status: 502 });
  }
}
