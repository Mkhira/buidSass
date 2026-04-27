/**
 * T035 — proxies media-upload signed-URL requests to the storage service
 * (spec 003) with `meta.draft = true`. Until the storage endpoint exists
 * the route returns a 501 so the client surfaces a clear gap signal.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";

export async function POST(
  request: Request,
  { params }: { params: { productId: string } },
) {
  const session = await getSession();
  if (!hasPermission(session, "catalog.product.write")) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }
  const body = await request.json().catch(() => ({}));
  // Spec 003 storage signed-URL endpoint not yet wired through proxyFetch.
  // Returning 501 surfaces the gap; the upload manager catches and renders
  // an error toast keyed off the body's reason field.
  return NextResponse.json(
    {
      error: "storage_signed_url_unavailable",
      reason: "Storage signed-URL issuer not yet wired (spec 003 gap).",
      productId: params.productId,
      requestedFilename: body?.filename ?? null,
    },
    { status: 501 },
  );
}
