/**
 * Create-product proxy. Server-only — wraps `proxyFetch` so client
 * forms can persist drafts via fetch() without pulling next/headers
 * into the client bundle.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { proxyFetch } from "@/lib/api/proxy";

export async function POST(request: Request) {
  const session = await getSession();
  if (!hasPermission(session, "catalog.product.write")) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }
  const body = await request.json().catch(() => ({}));
  const idempotencyKey = request.headers.get("Idempotency-Key") ?? undefined;
  try {
    const result = await proxyFetch("/v1/admin/catalog/products", {
      method: "POST",
      body: JSON.stringify(body),
      idempotencyKey,
    });
    return NextResponse.json(result);
  } catch (err) {
    const message = err instanceof Error ? err.message : "unknown";
    const status = /\b412\b/.test(message) ? 412 : 502;
    return NextResponse.json({ error: message }, { status });
  }
}
