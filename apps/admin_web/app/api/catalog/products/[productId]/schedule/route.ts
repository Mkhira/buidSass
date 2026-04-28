/**
 * T037 — schedule proxy.
 *
 * Spec 005 may carve schedule into its own endpoint or fold it into
 * /publish?scheduledAt=…; this proxy targets /publish for now and the
 * client supplies the timestamp.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { proxyFetch } from "@/lib/api/proxy";

export async function POST(
  request: Request,
  { params }: { params: { productId: string } },
) {
  const session = await getSession();
  if (!hasPermission(session, "catalog.product.write")) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }
  const body = await request.json().catch(() => ({}));
  if (!body?.scheduledAt) {
    return NextResponse.json({ error: "scheduledAt_required" }, { status: 422 });
  }
  try {
    const result = await proxyFetch(
      `/v1/admin/catalog/products/${encodeURIComponent(params.productId)}/publish`,
      { method: "POST", body: JSON.stringify({ scheduledAt: body.scheduledAt }) },
    );
    return NextResponse.json(result);
  } catch (err) {
    const message = err instanceof Error ? err.message : "unknown";
    return NextResponse.json({ error: message }, { status: 502 });
  }
}
