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
  // Refund transitions require an FR-013 step-up assertion. Reject
  // before proxying so the backend never sees a refund without the
  // freshly-minted MFA proof, and forward the assertion on the proxy
  // call so backend enforcement can verify it.
  const stepUp = request.headers.get("X-StepUp-Assertion");
  if (params.machine === "refund" && !stepUp) {
    return NextResponse.json({ error: "step_up_required" }, { status: 403 });
  }
  const idempotencyKey =
    request.headers.get("Idempotency-Key") ?? crypto.randomUUID();
  try {
    const result = await proxyFetch(
      `/v1/admin/orders/${encodeURIComponent(params.orderId)}/transitions/${encodeURIComponent(params.machine)}`,
      {
        method: "POST",
        body: JSON.stringify(body),
        idempotencyKey,
        headers: stepUp ? { "X-StepUp-Assertion": stepUp } : undefined,
      },
    );
    return NextResponse.json(result);
  } catch (err) {
    const message = err instanceof Error ? err.message : "unknown";
    return NextResponse.json({ error: message }, { status: 502 });
  }
}
