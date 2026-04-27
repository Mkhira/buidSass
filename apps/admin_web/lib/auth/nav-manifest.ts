/**
 * T032d (FR-028g): Nav-manifest loader.
 *
 * Two modes:
 *  - `USE_STATIC_NAV_MANIFEST=1` (default until spec 004 ships its loader):
 *    composes the sidebar from build-time JSON files under
 *    `lib/auth/nav-manifest-static/`. Each module owns one file in its
 *    reserved order range per `contracts/nav-manifest.md`.
 *  - `USE_STATIC_NAV_MANIFEST=0`: fetches from spec 004's
 *    `/v1/admin/nav-manifest` endpoint via `identityApi.navManifest()`.
 *
 * Cutover is one env-flip; contribution files don't move.
 */
import { identityApi } from "@/lib/api/clients/identity";
import { hasAllPermissions } from "./permissions";
import type { AdminSessionPayload } from "./session";

import foundation from "./nav-manifest-static/foundation.json";
import catalog from "./nav-manifest-static/catalog.json";
import inventory from "./nav-manifest-static/inventory.json";
import orders from "./nav-manifest-static/orders.json";

export interface NavEntry {
  id: string;
  labelKey: string;
  iconKey?: string;
  route: string;
  requiredPermissions: string[];
  order: number;
  badgeCountKey?: string | null;
}

export interface NavGroup {
  groupId: string;
  labelKey: string;
  iconKey?: string;
  order: number;
  entries: NavEntry[];
}

const STATIC_GROUPS: NavGroup[] = [
  foundation as NavGroup,
  catalog as NavGroup,
  inventory as NavGroup,
  orders as NavGroup,
];

function isStaticMode(): boolean {
  return process.env.USE_STATIC_NAV_MANIFEST !== "0";
}

/**
 * Returns the manifest groups visible to the given session, with each
 * group's entries filtered to those whose `requiredPermissions` are all
 * satisfied. Empty groups are dropped.
 */
export async function loadNavManifest(session: AdminSessionPayload): Promise<NavGroup[]> {
  const groups = isStaticMode() ? STATIC_GROUPS : await loadFromServer();
  return groups
    .map((g) => ({
      ...g,
      entries: g.entries
        .filter((e) => hasAllPermissions(session, e.requiredPermissions))
        .sort((a, b) => a.order - b.order),
    }))
    .filter((g) => g.entries.length > 0)
    .sort((a, b) => a.order - b.order);
}

async function loadFromServer(): Promise<NavGroup[]> {
  try {
    const result = await identityApi.navManifest();
    return result.groups.map((g) => ({
      groupId: g.id,
      labelKey: g.labelKey,
      iconKey: g.iconKey,
      order: g.order,
      entries: g.entries.map((e) => ({
        id: e.id,
        labelKey: e.labelKey,
        iconKey: e.iconKey,
        route: e.route,
        requiredPermissions: e.requiredPermissions,
        order: e.order,
        badgeCountKey: e.badgeCountKey ?? null,
      })),
    }));
  } catch {
    // Fall back to static if the endpoint isn't reachable.
    // The drift CI check (T032c / T032e) is the canonical alarm.
    return STATIC_GROUPS;
  }
}
