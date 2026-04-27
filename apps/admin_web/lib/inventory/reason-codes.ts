/**
 * T010 — Reason-codes catalog.
 *
 * Keys come from the server (R4); this module re-exports the
 * `requiresNote` predicate from `adjust-state` (single source of
 * truth). Server-side fetching lives in the route handler under
 * `app/api/inventory/reason-codes/route.ts` so client bundles never
 * pull `lib/api/proxy.ts` (which depends on `next/headers`).
 */
import type { ReasonCode } from "@/lib/api/clients/inventory";

export type { ReasonCode };

export { requiresNote } from "./adjust-state";
