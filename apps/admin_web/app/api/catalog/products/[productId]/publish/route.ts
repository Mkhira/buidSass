/**
 * T036 — publish proxy.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { proxyFetch } from "@/lib/api/proxy";

interface PublishBody {
  scheduledAt?: string | null;
}

export async function POST(
  request: Request,
  { params }: { params: { productId: string } },
) {
  const session = await getSession();
  if (!hasPermission(session, "catalog.product.write")) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }
  const body = (await request.json().catch(() => ({}))) as PublishBody;
  try {
    const result = await proxyFetch(
      `/v1/admin/catalog/products/${encodeURIComponent(params.productId)}/publish`,
      { method: "POST", body: JSON.stringify(body) },
    );
    return NextResponse.json(result);
  } catch (err) {
    const message = err instanceof Error ? err.message : "unknown";
    return NextResponse.json({ error: message }, { status: 502 });
  }
}
