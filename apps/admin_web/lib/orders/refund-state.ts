/**
 * T010 — SM-1 (refund draft submission).
 *
 * Idle → Drafting → Validating → StepUpRequired? → Submitting →
 *   {Submitted, ConflictDetected, OverRefundBlocked, Failed, FailedTerminal}
 *
 * Pure functions consumed by the refund form Bloc. The form's React
 * state holds the current `RefundSubmissionState`.
 */
import type { LineItem } from "@/lib/api/clients/orders";

export type RefundSubmissionState =
  | { kind: "idle" }
  | { kind: "drafting" }
  | { kind: "validating" }
  | { kind: "step_up_required"; idempotencyKey: string }
  | { kind: "submitting"; idempotencyKey: string; stepUpAssertionId: string | null }
  | { kind: "submitted"; refundId: string }
  | { kind: "over_refund_blocked"; perLineCaps: Record<string, number> }
  | { kind: "missing_reason_blocked" }
  | { kind: "conflict_detected" }
  | { kind: "failed"; reason: string; idempotencyKey: string }
  | { kind: "failed_terminal"; reasonCode: string };

export interface RefundLineDraft {
  lineId: string;
  qty: number;
  amountMinor: number;
}

export interface RefundDraftValidationInput {
  lines: RefundLineDraft[];
  reasonNote: string;
  /** From OrderDetail.lineItems — supplies deliveredQty + alreadyRefundedQty. */
  lineItems: LineItem[];
  /** Total amount being refunded vs. the order's grand total — drives the step-up cutover. */
  refundAmountMinor: number;
  grandTotalMinor: number;
  /** Per-market threshold above which step-up is required. */
  stepUpThresholdMinor: number;
}

/**
 * Computes the per-line cap (deliveredQty - alreadyRefundedQty) and
 * returns `over_refund_blocked` if any line exceeds it OR if any line's
 * amountMinor exceeds the refundable subtotal for that line.
 */
export function checkOverRefund(
  input: RefundDraftValidationInput,
): RefundSubmissionState | null {
  const perLineCaps: Record<string, number> = {};
  for (const line of input.lines) {
    const item = input.lineItems.find((li) => li.id === line.lineId);
    if (!item) continue;
    const cap = Math.max(0, item.deliveredQty - item.alreadyRefundedQty);
    perLineCaps[line.lineId] = cap;
    if (line.qty > cap) {
      return { kind: "over_refund_blocked", perLineCaps };
    }
    // Per-line amount cap: cannot refund more than the refundable
    // subtotal for that line (unitPriceMinor × cap).
    const maxAmount = item.unitPriceMinor * cap;
    if (line.amountMinor > maxAmount) {
      return { kind: "over_refund_blocked", perLineCaps };
    }
  }
  return null;
}

/**
 * Step-up MFA required when refunding the full grand total OR when the
 * refund amount exceeds the per-market threshold. Return value drives the
 * `<StepUpDialog>` mount.
 */
export function requiresStepUp(input: {
  refundAmountMinor: number;
  grandTotalMinor: number;
  stepUpThresholdMinor: number;
}): boolean {
  return (
    input.refundAmountMinor >= input.grandTotalMinor ||
    input.refundAmountMinor > input.stepUpThresholdMinor
  );
}

/** FR-005 — reason note required (≥10 chars, ≤2000). */
export function reasonNoteValid(note: string): boolean {
  const trimmed = note.trim();
  return trimmed.length >= 10 && trimmed.length <= 2000;
}

/**
 * Pure validation step. Returns the next state to surface OR null if
 * the draft is valid and ready to advance.
 */
export function validateRefundDraft(
  input: RefundDraftValidationInput,
): RefundSubmissionState | null {
  if (input.lines.length === 0) {
    // No lines selected — surface as over-refund blocked with empty
    // caps so the screen renders the "select at least one line" hint.
    return { kind: "over_refund_blocked", perLineCaps: {} };
  }
  if (!reasonNoteValid(input.reasonNote)) {
    return { kind: "missing_reason_blocked" };
  }
  const overRefund = checkOverRefund(input);
  if (overRefund !== null) return overRefund;
  return null;
}
