/**
 * T010 — Reason-codes catalog.
 *
 * Keys come from the server (R4) — this module enforces the
 * mandatory-note flag locally per FR-004 plus exports a thin
 * fetch helper. The actual react-query hook lives in the consuming
 * component's data layer; we keep the lib free of React deps.
 */
import { inventoryApi, type ReasonCode } from "@/lib/api/clients/inventory";

const NOTE_REQUIRED: ReadonlySet<string> = new Set([
  "theft_loss",
  "write_off_below_zero",
  "breakage",
]);

/** FR-004 — these reason codes ALWAYS require a ≥10-char note. */
export function requiresNote(reasonCode: string): boolean {
  return NOTE_REQUIRED.has(reasonCode);
}

/** Fetches the server-published catalog. UI caches via react-query. */
export async function fetchReasonCodes(): Promise<ReasonCode[]> {
  return inventoryApi.reasonCodes.list();
}
