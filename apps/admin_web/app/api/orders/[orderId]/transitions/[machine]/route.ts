/**
 * T036 — state-transition POST proxy with idempotency-key forwarding.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { proxyFetch } from "@/lib/api/proxy";

const VALID_MACHINES = new Set(["order", "payment", "fulfillment", "refund"]);

const MACHINE_PERMISSION: Record<string, string> = {
  order: "orders.transition.order",
  payment: "orders.transition.payment",
  fulfillment: "orders.transition.fulfillment",
  refund: "orders.transition.refund",
};

export async function POST(
  request: Request,
  { params }: { params: { orderId: string; machine: string } },
) {
  if (!VALID_MACHINES.has(params.machine)) {
    return NextResponse.json({ error: "invalid_machine" }, { status: 400 });
  }
  const session = await getSession();
  if (!hasPermission(session, MACHINE_PERMISSION[params.machine])) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }
  const body = await request.json().catch(() => null);
  if (!body) {
    return NextResponse.json({ error: "invalid_body" }, { status: 422 });
  }
  const idempotencyKey =
    request.headers.get("Idempotency-Key") ?? crypto.randomUUID();
  try {
    const result = await proxyFetch(
      `/v1/admin/orders/${encodeURIComponent(params.orderId)}/transitions/${encodeURIComponent(params.machine)}`,
      { method: "POST", body: JSON.stringify(body), idempotencyKey },
    );
    return NextResponse.json(result);
  } catch (err) {
    const message = err instanceof Error ? err.message : "unknown";
    return NextResponse.json({ error: message }, { status: 502 });
  }
}
