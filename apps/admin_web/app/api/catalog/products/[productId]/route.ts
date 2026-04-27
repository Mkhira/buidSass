/**
 * Update-product proxy. Server-only — wraps `proxyFetch` so client
 * forms can PUT updates via fetch() without pulling next/headers into
 * the client bundle.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { proxyFetch } from "@/lib/api/proxy";

export async function PUT(
  request: Request,
  { params }: { params: { productId: string } },
) {
  const session = await getSession();
  if (!hasPermission(session, "catalog.product.write")) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }
  const body = await request.json().catch(() => ({}));
  try {
    const result = await proxyFetch(
      `/v1/admin/catalog/products/${encodeURIComponent(params.productId)}`,
      { method: "PUT", body: JSON.stringify(body) },
    );
    return NextResponse.json(result);
  } catch (err) {
    const message = err instanceof Error ? err.message : "unknown";
    const status = /\b412\b/.test(message) ? 412 : 502;
    return NextResponse.json({ error: message }, { status });
  }
}
