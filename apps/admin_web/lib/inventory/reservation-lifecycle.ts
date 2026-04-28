/**
 * T008 — SM-3 (reservation lifecycle).
 *
 * The reservations screen surfaces only Active reservations. Released /
 * Expired entries appear in the audit-log reader (spec 015) and the
 * ledger as movement context. The TTL countdown is driven by a single
 * 1-Hz ticker (FR-016a) — never per-row server polling.
 */
import type { Reservation } from "@/lib/api/clients/inventory";

export type ReservationLifecycleState = "active" | "released" | "expired";

export function reservationStateFor(
  reservation: Reservation,
  now: Date,
): ReservationLifecycleState {
  const expires = new Date(reservation.expiresAt);
  if (expires.getTime() <= now.getTime()) return "expired";
  return "active";
}

export function ttlSecondsRemaining(
  reservation: Reservation,
  now: Date,
): number {
  const expires = new Date(reservation.expiresAt).getTime();
  const remaining = (expires - now.getTime()) / 1000;
  return Math.max(0, Math.floor(remaining));
}
