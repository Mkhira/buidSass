/**
 * T032: Permission helpers + per-route permission map.
 *
 * The map mirrors `contracts/routes.md`; new routes register their
 * `requiredPermissions` here. Middleware (T026 / T032a) calls
 * `permissionsForRoute(pathname)` to enforce on direct navigation.
 *
 * `permission-catalog.md` is the single source of truth for the **set**
 * of permission keys; the drift check (T032c) verifies this map only
 * uses keys that exist there.
 */
import type { AdminSessionPayload } from "./session";

export interface RoutePermissionRule {
  /** Matched against the request's pathname. Trailing wildcards supported via `*`. */
  pattern: RegExp;
  /** Keys required (logical AND). Empty = session-active is sufficient. */
  requiredPermissions: string[];
}

export const ROUTE_PERMISSIONS: RoutePermissionRule[] = [
  // Audit reader
  { pattern: /^\/audit(?:\/|$)/, requiredPermissions: ["audit.read"] },
  // Identity surfaces — session-active is sufficient
  { pattern: /^\/me(?:\/|$)/, requiredPermissions: [] },
  // Catalog (registered when spec 016 lands)
  { pattern: /^\/catalog\/?$/, requiredPermissions: ["catalog.read"] },
  { pattern: /^\/catalog\/products(?:\/|$)/, requiredPermissions: ["catalog.product.read"] },
  { pattern: /^\/catalog\/categories(?:\/|$)/, requiredPermissions: ["catalog.category.read"] },
  { pattern: /^\/catalog\/brands(?:\/|$)/, requiredPermissions: ["catalog.brand.read"] },
  { pattern: /^\/catalog\/manufacturers(?:\/|$)/, requiredPermissions: ["catalog.manufacturer.read"] },
  { pattern: /^\/catalog\/bulk-import(?:\/|$)/, requiredPermissions: ["catalog.product.bulk_import"] },
  // Inventory (spec 017)
  { pattern: /^\/inventory(?:\/|$)/, requiredPermissions: ["inventory.read"] },
  // Orders (spec 018)
  { pattern: /^\/orders(?:\/|$)/, requiredPermissions: ["orders.read"] },
  // Customers (spec 019)
  { pattern: /^\/customers(?:\/|$)/, requiredPermissions: ["customers.read"] },
];

export function hasPermission(session: AdminSessionPayload | null, key: string): boolean {
  if (!session) return false;
  return session.permissions.includes(key);
}

export function hasAllPermissions(session: AdminSessionPayload | null, keys: string[]): boolean {
  if (keys.length === 0) return Boolean(session);
  if (!session) return false;
  return keys.every((k) => session.permissions.includes(k));
}

/** Returns the required-permission set for a given pathname, or `null` if unknown. */
export function permissionsForRoute(pathname: string): string[] | null {
  for (const rule of ROUTE_PERMISSIONS) {
    if (rule.pattern.test(pathname)) return rule.requiredPermissions;
  }
  return null;
}
