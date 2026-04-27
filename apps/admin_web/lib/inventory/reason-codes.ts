/**
 * T010 — Reason-codes catalog.
 *
 * Keys come from the server (R4); this module re-exports the
 * `requiresNote` predicate from `adjust-state` (single source of
 * truth) plus a thin fetch helper. The actual react-query hook
 * lives in the consuming component's data layer.
 */
import { inventoryApi, type ReasonCode } from "@/lib/api/clients/inventory";

export { requiresNote } from "./adjust-state";

/** Fetches the server-published catalog. UI caches via react-query. */
export async function fetchReasonCodes(): Promise<ReasonCode[]> {
  return inventoryApi.reasonCodes.list();
}
