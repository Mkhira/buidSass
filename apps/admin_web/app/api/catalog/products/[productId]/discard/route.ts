/**
 * Discard-product proxy. Server-only.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { proxyFetch } from "@/lib/api/proxy";

export async function POST(
  _request: Request,
  { params }: { params: { productId: string } },
) {
  const session = await getSession();
  if (!hasPermission(session, "catalog.product.write")) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }
  try {
    await proxyFetch(
      `/v1/admin/catalog/products/${encodeURIComponent(params.productId)}/discard`,
      { method: "POST" },
    );
    return NextResponse.json({ ok: true });
  } catch (err) {
    const message = err instanceof Error ? err.message : "unknown";
    return NextResponse.json({ error: message }, { status: 502 });
  }
}
