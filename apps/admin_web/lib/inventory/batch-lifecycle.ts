/**
 * T007 — SM-2 (batch lifecycle).
 *
 * States derive from server fields; the UI computes lane assignment +
 * pill state without async work.
 */
import type { Batch } from "@/lib/api/clients/inventory";

export type BatchLifecycleState =
  | "active"
  | "near_expiry"
  | "expired"
  | "written_off";

export const DEFAULT_NEAR_EXPIRY_THRESHOLD_DAYS = 30;

export function batchStateFor(
  batch: Batch,
  now: Date,
  thresholdDays: number = DEFAULT_NEAR_EXPIRY_THRESHOLD_DAYS,
): BatchLifecycleState {
  if (batch.onHand <= 0) return "written_off";
  const expires = new Date(batch.expiresOn);
  if (expires.getTime() < now.getTime()) return "expired";
  const ms = expires.getTime() - now.getTime();
  const days = ms / (1000 * 60 * 60 * 24);
  if (days <= thresholdDays) return "near_expiry";
  return "active";
}

export interface ExpiryLane {
  kind: "near_expiry" | "expired" | "future";
  thresholdDays: number;
  batches: Batch[];
}

export function laneFor(state: BatchLifecycleState): ExpiryLane["kind"] {
  switch (state) {
    case "near_expiry":
      return "near_expiry";
    case "expired":
      return "expired";
    case "active":
    case "written_off":
      return "future";
  }
}

export function bucketBatchesIntoLanes(
  batches: Batch[],
  now: Date,
  thresholdDays: number = DEFAULT_NEAR_EXPIRY_THRESHOLD_DAYS,
): ExpiryLane[] {
  const buckets: Record<ExpiryLane["kind"], Batch[]> = {
    near_expiry: [],
    expired: [],
    future: [],
  };
  for (const b of batches) {
    if (b.onHand <= 0) continue; // written-off — not surfaced here
    const state = batchStateFor(b, now, thresholdDays);
    buckets[laneFor(state)].push(b);
  }
  return [
    { kind: "near_expiry", thresholdDays, batches: buckets.near_expiry },
    { kind: "expired", thresholdDays, batches: buckets.expired },
    { kind: "future", thresholdDays, batches: buckets.future },
  ];
}
