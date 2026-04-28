/**
 * GET /api/notifications/unread — proxies the stub feed (or spec 023 once
 * shipped) to the BellMenu Client Component.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { notificationsApi } from "@/lib/api/clients/notifications";

export async function GET() {
  const session = await getSession();
  if (!session) {
    return NextResponse.json({ entries: [], unreadCount: 0 }, { status: 401 });
  }
  try {
    const result = await notificationsApi.unread();
    return NextResponse.json(result, {
      headers: { "Cache-Control": "private, max-age=10" },
    });
  } catch {
    return NextResponse.json({ entries: [], unreadCount: 0 });
  }
}
