/**
 * SM-1 — Account action submission.
 *
 * Idle → Confirming → StepUpRequired → StepUpInProgress → Submitting →
 *   Submitted / ConflictDetected / Failed / FailedTerminal
 *
 * Step-up MFA is REQUIRED for every account action (FR-013).
 */

export type AccountActionKind =
  | "suspend"
  | "unlock"
  | "password_reset_trigger";

export type AccountActionSubmissionState =
  | { kind: "idle" }
  | { kind: "confirming"; action: AccountActionKind }
  | {
      kind: "step_up_required";
      action: AccountActionKind;
      reasonNote: string;
      idempotencyKey: string;
    }
  | {
      kind: "step_up_in_progress";
      action: AccountActionKind;
      reasonNote: string;
      idempotencyKey: string;
    }
  | { kind: "step_up_failed"; reason: string }
  | {
      kind: "submitting";
      action: AccountActionKind;
      reasonNote: string;
      idempotencyKey: string;
      stepUpAssertionId: string;
    }
  | { kind: "submitted" }
  | { kind: "conflict_detected" }
  | {
      kind: "failed";
      reason: string;
      action: AccountActionKind;
      reasonNote: string;
      idempotencyKey: string;
    }
  | { kind: "failed_terminal"; reasonCode: string };

/** FR-005 — reason note ≥10 chars and ≤2000. */
export function reasonNoteValid(note: string): boolean {
  const trimmed = note.trim();
  return trimmed.length >= 10 && trimmed.length <= 2000;
}

/**
 * Self-action guard — admins cannot suspend / unlock / reset their own
 * account. Both client AND server enforce; this is the client check.
 */
export function isSelfAction(input: {
  targetCustomerId: string;
  currentSessionAdminId: string;
}): boolean {
  return input.targetCustomerId === input.currentSessionAdminId;
}

export interface PreSubmitInput {
  action: AccountActionKind;
  reasonNote: string;
  targetCustomerId: string;
  currentSessionAdminId: string;
}

/**
 * Pre-submit validation. Returns null on pass; an error state on fail.
 * Caller transitions Confirming → StepUpRequired on null.
 */
export function preSubmitCheck(
  input: PreSubmitInput,
): AccountActionSubmissionState | null {
  if (
    isSelfAction({
      targetCustomerId: input.targetCustomerId,
      currentSessionAdminId: input.currentSessionAdminId,
    })
  ) {
    return {
      kind: "failed_terminal",
      reasonCode: "customers.self_action_forbidden",
    };
  }
  if (!reasonNoteValid(input.reasonNote)) {
    // Stays in confirming — UI surfaces the inline message.
    return { kind: "confirming", action: input.action };
  }
  return null;
}
