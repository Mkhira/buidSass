/**
 * T034: GET /api/nav-manifest
 *
 * Returns the role-filtered manifest for the active admin. Cached
 * `private, max-age=60` so the sidebar doesn't refetch on every nav.
 */
import { NextResponse } from "next/server";
import { getSession } from "@/lib/auth/session";
import { loadNavManifest } from "@/lib/auth/nav-manifest";

export async function GET() {
  const session = await getSession();
  if (!session) {
    return NextResponse.json({ groups: [] }, { status: 401 });
  }
  const groups = await loadNavManifest(session);
  return NextResponse.json(
    { groups },
    {
      headers: {
        "Cache-Control": "private, max-age=60",
      },
    },
  );
}
