/**
 * T006 — SM-1 (adjustment submission).
 *
 * Idle → Validating → Submitting → Submitted
 *                              ├─ ConflictDetected (412)
 *                              ├─ BelowZeroBlocked (client-side gate)
 *                              ├─ MissingNoteBlocked (client-side gate)
 *                              ├─ Failed (5xx / network)
 *                              └─ FailedTerminal (inventory.permission_revoked)
 */

export type AdjustmentSubmissionState =
  | { kind: "idle" }
  | { kind: "validating" }
  | { kind: "submitting"; idempotencyKey: string }
  | { kind: "submitted"; ledgerEntryId: string }
  | { kind: "conflict_detected"; reasonCode: "412" }
  | { kind: "below_zero_blocked"; available: number; delta: number }
  | { kind: "missing_note_blocked"; reasonCode: string }
  | { kind: "failed"; reason: string; idempotencyKey: string }
  | { kind: "failed_terminal"; reasonCode: "inventory.permission_revoked" };

export interface AdjustmentValidationInput {
  delta: number;
  reasonCode: string;
  note: string;
  onHand: number;
  hasWriteoffBelowZeroPermission: boolean;
}

const NOTE_REQUIRED_REASONS: ReadonlySet<string> = new Set([
  "theft_loss",
  "write_off_below_zero",
  "breakage",
]);

/** FR-004 — note required for these reason codes (≥ 10 chars). */
export function requiresNote(reasonCode: string): boolean {
  return NOTE_REQUIRED_REASONS.has(reasonCode);
}

/** FR-005 — block-by-default on below-zero adjustment unless permission held. */
export function wouldBeBelowZero(input: {
  delta: number;
  onHand: number;
}): boolean {
  return input.onHand + input.delta < 0;
}

/**
 * Pure validation step. Returns the next state for the form to surface.
 * Does NOT submit — the caller wires this into a SubmitTapped handler.
 */
export function validateAdjustment(
  input: AdjustmentValidationInput,
): AdjustmentSubmissionState {
  if (requiresNote(input.reasonCode) && input.note.trim().length < 10) {
    return { kind: "missing_note_blocked", reasonCode: input.reasonCode };
  }
  if (
    wouldBeBelowZero(input) &&
    !input.hasWriteoffBelowZeroPermission
  ) {
    return {
      kind: "below_zero_blocked",
      available: input.onHand,
      delta: input.delta,
    };
  }
  return { kind: "validating" };
}
