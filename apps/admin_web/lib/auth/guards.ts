/**
 * Server Component helpers — `requireSession()` + `requirePermission()`.
 *
 * Replaces the pattern that 016/017/018/019 would otherwise duplicate
 * ~50 times across feature pages:
 *
 *   const session = await getSession();
 *   if (!session) redirect("/login");
 *   if (!hasPermission(session, "...")) redirect("/__forbidden");
 *
 * with:
 *
 *   const session = await requirePermission("orders.read");
 *
 * The helpers throw via `redirect()` (which Next.js handles by short-
 * circuiting the render) so the caller's return type is just
 * `AdminSessionPayload` — no null-checks downstream.
 */
import { redirect } from "next/navigation";
import { getSession, type AdminSessionPayload } from "./session";
import { hasAllPermissions } from "./permissions";

/**
 * Throws (via redirect) to /login if no session. Returns the session
 * otherwise.
 */
export async function requireSession(continueTo?: string): Promise<AdminSessionPayload> {
  const session = await getSession();
  if (!session) {
    const target = continueTo
      ? `/login?continueTo=${encodeURIComponent(continueTo)}`
      : "/login";
    redirect(target);
  }
  return session;
}

/**
 * Throws (via redirect) to /login if no session, /__forbidden if the
 * permission check fails. Returns the session otherwise.
 *
 * Accepts a single permission key or an array — array semantics are
 * logical AND (every key must be held).
 */
export async function requirePermission(
  permissions: string | string[],
  continueTo?: string,
): Promise<AdminSessionPayload> {
  const session = await requireSession(continueTo);
  const keys = typeof permissions === "string" ? [permissions] : permissions;
  if (!hasAllPermissions(session, keys)) {
    redirect("/__forbidden");
  }
  return session;
}

/**
 * Throws (via redirect) when the actor lacks ANY of the listed permissions.
 * Returns the session + the subset they DO hold.
 */
export async function requireAnyPermission(
  permissions: string[],
  continueTo?: string,
): Promise<{ session: AdminSessionPayload; held: string[] }> {
  const session = await requireSession(continueTo);
  const held = permissions.filter((k) => session.permissions.includes(k));
  if (held.length === 0) redirect("/__forbidden");
  return { session, held };
}
