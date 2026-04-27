/**
 * T040c: StepUpDialog (FR-025 + spec 018 FR-015 + spec 019 FR-013).
 *
 * Wraps spec 004's step-up MFA flow. Calls `/api/auth/step-up/start` to
 * obtain a `challengeId`, prompts for the TOTP code, calls
 * `/api/auth/step-up/complete` to obtain an `assertionId`, then resolves
 * with the assertion. Callers attach the assertion id as
 * `X-StepUp-Assertion` on the gated mutation.
 *
 * Used by:
 *  - Spec 018 refunds (above env threshold or full-amount)
 *  - Spec 019 account actions (suspend / unlock / password-reset trigger)
 */
"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Alert, AlertDescription } from "@/components/ui/alert";

export interface StepUpResult {
  assertionId: string;
  expiresAt: number;
}

export interface StepUpDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: (result: StepUpResult) => void;
  /** Optional retry hint when a previous assertion expired. */
  retryReason?: "expired" | "rejected";
}

interface DialogState {
  phase: "idle" | "starting" | "awaiting_code" | "verifying" | "error" | "no_factor";
  challengeId?: string;
  errorReason?: string;
}

export function StepUpDialog({ open, onOpenChange, onSuccess, retryReason }: StepUpDialogProps) {
  const t = useTranslations("shell.step_up");
  const [state, setState] = useState<DialogState>({ phase: "idle" });
  const [code, setCode] = useState("");

  useEffect(() => {
    if (!open) {
      setState({ phase: "idle" });
      setCode("");
      return;
    }
    void startChallenge();
  }, [open]);

  async function startChallenge() {
    setState({ phase: "starting" });
    try {
      const res = await fetch("/api/auth/step-up/start", { method: "POST" });
      if (res.status === 412) {
        const body = await res.json().catch(() => ({}));
        setState({ phase: "no_factor", errorReason: body.reasonCode });
        return;
      }
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setState({ phase: "error", errorReason: body.reasonCode ?? `http_${res.status}` });
        return;
      }
      const body = (await res.json()) as { challengeId: string };
      setState({ phase: "awaiting_code", challengeId: body.challengeId });
    } catch {
      setState({ phase: "error", errorReason: "network" });
    }
  }

  async function submitCode() {
    if (!state.challengeId) return;
    setState({ phase: "verifying", challengeId: state.challengeId });
    try {
      const res = await fetch("/api/auth/step-up/complete", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ challengeId: state.challengeId, code }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setState({
          phase: "awaiting_code",
          challengeId: state.challengeId,
          errorReason: body.reasonCode ?? `http_${res.status}`,
        });
        return;
      }
      const result = (await res.json()) as StepUpResult;
      onSuccess(result);
      onOpenChange(false);
    } catch {
      setState({ phase: "awaiting_code", challengeId: state.challengeId, errorReason: "network" });
    }
  }

  const pending = state.phase === "starting" || state.phase === "verifying";

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("title")}</DialogTitle>
          <DialogDescription>{t("body")}</DialogDescription>
        </DialogHeader>

        {retryReason === "expired" ? (
          <Alert variant="destructive">
            <AlertDescription>{t("expired")}</AlertDescription>
          </Alert>
        ) : null}

        {state.phase === "no_factor" ? (
          <Alert variant="destructive">
            <AlertDescription>{t("no_factor_enrolled")}</AlertDescription>
          </Alert>
        ) : null}

        {state.errorReason && state.phase !== "no_factor" ? (
          <Alert variant="destructive">
            <AlertDescription className="font-mono text-xs">{state.errorReason}</AlertDescription>
          </Alert>
        ) : null}

        <div className="space-y-ds-sm">
          <Label htmlFor="step-up-code">{t("totp_label")}</Label>
          <Input
            id="step-up-code"
            inputMode="numeric"
            autoComplete="one-time-code"
            pattern="\d{6}"
            maxLength={6}
            value={code}
            onChange={(e) => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
            disabled={pending || state.phase === "no_factor"}
            aria-busy={pending}
          />
        </div>

        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)} disabled={pending}>
            {t("cancel")}
          </Button>
          <Button
            type="button"
            onClick={submitCode}
            disabled={pending || code.length !== 6 || state.phase === "no_factor"}
          >
            {t("submit")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
