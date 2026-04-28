/**
 * `<RequirePermission>` — declarative gate for action buttons + side
 * panels in 016/017/018/019. Renders `children` iff the actor holds
 * the listed permission(s); otherwise renders `fallback` (defaults to
 * null, matching FR-010 hide-not-disable).
 *
 * Example:
 *   <RequirePermission keys="orders.refund.initiate">
 *     <RefundButton />
 *   </RequirePermission>
 *
 *   <RequirePermission keys={["orders.cancel", "orders.read"]} all>
 *     <CancelOrderButton />
 *   </RequirePermission>
 */
"use client";

import type { ReactNode } from "react";
import { useSession } from "@/components/providers/session-provider";

interface RequirePermissionProps {
  /** Single permission key or array of keys. */
  keys: string | string[];
  /** When `true` (default), every key must be held (AND). When `false`, any one key suffices (OR). */
  all?: boolean;
  /** Optional fallback rendered when the gate fails. Defaults to nothing (FR-010 hide-not-disable). */
  fallback?: ReactNode;
  children: ReactNode;
}

export function RequirePermission({ keys, all = true, fallback = null, children }: RequirePermissionProps) {
  const session = useSession();
  const list = typeof keys === "string" ? [keys] : keys;
  const allowed = all
    ? list.every((k) => session.permissions.has(k))
    : list.some((k) => session.permissions.has(k));
  return <>{allowed ? children : fallback}</>;
}
