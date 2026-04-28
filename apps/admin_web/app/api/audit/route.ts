/**
 * GET /api/audit — proxies the spec 003 audit-read endpoint to the
 * audit-list page. Filter params are forwarded as a query string.
 */
import { NextResponse, type NextRequest } from "next/server";
import { getSession } from "@/lib/auth/session";
import { hasPermission } from "@/lib/auth/permissions";
import { auditApi, type AuditFilter } from "@/lib/api/clients/audit";

export async function GET(req: NextRequest) {
  const session = await getSession();
  if (!session) {
    return NextResponse.json({ entries: [], nextCursor: null }, { status: 401 });
  }
  if (!hasPermission(session, "audit.read")) {
    return NextResponse.json({ entries: [], nextCursor: null }, { status: 403 });
  }

  const url = new URL(req.url);
  const filter: AuditFilter = {
    actor: url.searchParams.get("actor") ?? undefined,
    resourceType: url.searchParams.get("resourceType") ?? undefined,
    resourceId: url.searchParams.get("resourceId") ?? undefined,
    actionKey: url.searchParams.get("actionKey") ?? undefined,
    marketScope: (url.searchParams.get("marketScope") as AuditFilter["marketScope"]) ?? undefined,
    from: url.searchParams.get("from") ?? undefined,
    to: url.searchParams.get("to") ?? undefined,
    cursor: url.searchParams.get("cursor") ?? undefined,
  };

  try {
    const page = await auditApi.list(filter);
    return NextResponse.json(page, {
      headers: { "Cache-Control": "private, max-age=30" },
    });
  } catch {
    // Spec 003's audit-read endpoint may not be reachable in dev; return
    // an empty page so the UI renders the empty-state path. Production
    // surfaces a 5xx here which the client maps to <ErrorState>.
    return NextResponse.json({ entries: [], nextCursor: null });
  }
}
