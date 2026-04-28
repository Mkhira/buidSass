/**
 * Account action bar — suspend / unlock / password-reset-trigger.
 *
 * Per FR-013, every account action requires step-up MFA. The action
 * bar opens the confirmation dialog → step-up dialog → submit.
 *
 * The step-up flow itself reuses spec 015's `<StepUpDialog>` (T040c).
 * For v1 we mock the assertion until that dialog ships — the API
 * proxy still forwards `X-StepUp-Assertion`.
 */
"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  isSelfAction,
  reasonNoteValid,
  type AccountActionKind,
  type AccountActionSubmissionState,
} from "@/lib/customers/account-action-state";

export interface AccountActionBarProps {
  customerId: string;
  rowVersion: number;
  currentSessionAdminId: string;
  permissions: string[];
  accountState: "active" | "suspended" | "closed";
}

export function AccountActionBar({
  customerId,
  rowVersion,
  currentSessionAdminId,
  permissions,
  accountState,
}: AccountActionBarProps) {
  const router = useRouter();
  const t = useTranslations("customers.actions");
  const tCommon = useTranslations("common");
  const [submission, setSubmission] = useState<AccountActionSubmissionState>({
    kind: "idle",
  });

  // Map SM-1 action kind to its localized label key. The state-machine
  // identifier `password_reset_trigger` differs from the i18n key
  // (`password_reset`) so we keep the mapping explicit.
  function actionLabelKey(action: AccountActionKind): string {
    return action === "password_reset_trigger" ? "password_reset" : action;
  }
  const [reasonNote, setReasonNote] = useState("");
  const permSet = useMemo(() => new Set(permissions), [permissions]);
  const canAct = permSet.has("customers.account_action");

  const isSelf = isSelfAction({
    targetCustomerId: customerId,
    currentSessionAdminId,
  });
  if (isSelf) {
    return (
      <p className="text-sm text-muted-foreground">
        {t("self_action_forbidden")}
      </p>
    );
  }
  if (!canAct) return null;

  const buttons: Array<{ kind: AccountActionKind; labelKey: string; show: boolean }> = [
    {
      kind: "suspend",
      labelKey: "suspend",
      show: accountState === "active",
    },
    {
      kind: "unlock",
      labelKey: "unlock",
      show: accountState === "suspended",
    },
    {
      kind: "password_reset_trigger",
      labelKey: "password_reset",
      show: accountState !== "closed",
    },
  ];

  const dialog =
    submission.kind === "confirming" ||
    submission.kind === "step_up_required" ||
    submission.kind === "step_up_in_progress" ||
    submission.kind === "submitting"
      ? submission
      : null;

  async function submit(action: AccountActionKind) {
    if (!reasonNoteValid(reasonNote)) return;
    const idempotencyKey = crypto.randomUUID();
    setSubmission({
      kind: "submitting",
      action,
      reasonNote,
      idempotencyKey,
      // Stub assertion until <StepUpDialog> wires the real spec 004 flow.
      // Server is authoritative; this client value is forwarded for tracing.
      stepUpAssertionId: "stub-step-up",
    });
    try {
      const path =
        action === "suspend"
          ? "suspend"
          : action === "unlock"
            ? "unlock"
            : "password-reset";
      const res = await fetch(
        `/api/customers/${encodeURIComponent(customerId)}/${path}`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Idempotency-Key": idempotencyKey,
            "X-StepUp-Assertion": "stub-step-up",
          },
          body: JSON.stringify({ reasonNote, rowVersion }),
        },
      );
      if (!res.ok) {
        const errBody = await res.json().catch(() => ({ error: `${res.status}` }));
        throw new Error(errBody.error ?? `${res.status}`);
      }
      setSubmission({ kind: "submitted" });
      setReasonNote("");
      router.refresh();
    } catch (err) {
      const message = err instanceof Error ? err.message : "unknown";
      if (message.includes("412")) {
        setSubmission({ kind: "conflict_detected" });
      } else if (message.includes("customers.permission_revoked")) {
        setSubmission({
          kind: "failed_terminal",
          reasonCode: "customers.permission_revoked",
        });
      } else {
        setSubmission({
          kind: "failed",
          reason: message,
          action,
          reasonNote,
          idempotencyKey,
        });
      }
    }
  }

  return (
    <>
      <div className="flex flex-wrap gap-ds-sm">
        {buttons
          .filter((b) => b.show)
          .map((b) => (
            <Button
              key={b.kind}
              type="button"
              variant={b.kind === "suspend" ? "destructive" : "outline"}
              onClick={() =>
                setSubmission({ kind: "confirming", action: b.kind })
              }
            >
              {t(actionLabelKey(b.kind) as never)}
            </Button>
          ))}
      </div>

      <Dialog
        open={dialog !== null}
        onOpenChange={(o) => {
          if (!o) setSubmission({ kind: "idle" });
        }}
      >
        <DialogContent>
          {dialog ? (
            <>
              <DialogHeader>
                <DialogTitle>
                  {t(actionLabelKey(dialog.action) as never)}
                </DialogTitle>
                <DialogDescription>
                  {t("step_up_required")}
                </DialogDescription>
              </DialogHeader>
              <div className="space-y-ds-sm">
                <label className="block text-sm font-medium">
                  {t("reason_label")}
                </label>
                <Textarea
                  value={reasonNote}
                  onChange={(e) => setReasonNote(e.target.value)}
                  rows={4}
                  aria-required
                  aria-describedby="reason-help"
                />
                <p id="reason-help" className="text-xs text-muted-foreground">
                  {t("reason_required_help")}
                </p>
              </div>
              <DialogFooter>
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => setSubmission({ kind: "idle" })}
                >
                  {tCommon("cancel")}
                </Button>
                <Button
                  type="button"
                  disabled={
                    !reasonNoteValid(reasonNote) ||
                    submission.kind === "submitting"
                  }
                  onClick={() => submit(dialog.action)}
                >
                  {t("submit")}
                </Button>
              </DialogFooter>
            </>
          ) : null}
        </DialogContent>
      </Dialog>

      {submission.kind === "conflict_detected" ? (
        <p role="alert" className="text-sm text-destructive">
          {t("stale_version")}
        </p>
      ) : null}
      {submission.kind === "failed_terminal" ? (
        <p role="alert" className="text-sm text-destructive">
          {t("permission_revoked")}
        </p>
      ) : null}
      {submission.kind === "failed" ? (
        <p role="alert" className="text-sm text-destructive">
          {submission.reason}
        </p>
      ) : null}
    </>
  );
}
