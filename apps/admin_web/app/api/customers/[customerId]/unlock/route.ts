import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { proxyFetch } from "@/lib/api/proxy";

export async function POST(
  request: Request,
  { params }: { params: { customerId: string } },
) {
  const session = await getSession();
  if (!hasPermission(session, "customers.account_action")) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }
  if (session?.adminId === params.customerId) {
    return NextResponse.json({ error: "self_action_forbidden" }, { status: 403 });
  }
  const body = await request.json().catch(() => null);
  if (!body) {
    return NextResponse.json({ error: "invalid_body" }, { status: 422 });
  }
  const stepUp = request.headers.get("X-StepUp-Assertion");
  if (!stepUp) {
    return NextResponse.json({ error: "step_up_required" }, { status: 403 });
  }
  const idempotencyKey =
    request.headers.get("Idempotency-Key") ?? crypto.randomUUID();
  try {
    const result = await proxyFetch(
      `/v1/admin/customers/${encodeURIComponent(params.customerId)}/unlock`,
      {
        method: "POST",
        body: JSON.stringify(body),
        idempotencyKey,
        headers: { "X-StepUp-Assertion": stepUp },
      },
    );
    return NextResponse.json(result);
  } catch (err) {
    const message = err instanceof Error ? err.message : "unknown";
    return NextResponse.json({ error: message }, { status: 502 });
  }
}
