/**
 * T033 — transition-action bar.
 *
 * Renders only `kind: 'render'` decisions (FR-010); `'render_disabled'`
 * rows render as disabled buttons with a localized tooltip.
 */
"use client";

import { useMemo, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  candidateTransitions,
  type Machine,
} from "@/lib/orders/transition-gate";

export interface TransitionActionBarProps {
  orderId: string;
  rowVersion: number;
  permissions: string[];
  /** Current state per machine — exactly one of these is the "active" machine. */
  states: Record<Machine, string>;
  orderClosed: boolean;
}

export function TransitionActionBar({
  orderId,
  rowVersion,
  permissions,
  states,
  orderClosed,
}: TransitionActionBarProps) {
  const router = useRouter();
  const t = useTranslations();
  const tErrors = useTranslations("orders.errors");
  const [, startTransition] = useTransition();
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const permSet = useMemo(() => new Set(permissions), [permissions]);

  const machines: Machine[] = ["order", "fulfillment", "payment"];
  const decisions = machines.flatMap((machine) =>
    candidateTransitions({
      machine,
      fromState: states[machine],
      permissions: permSet,
      orderClosed,
    }).map((decision) => ({ machine, decision })),
  );

  async function fire(machine: Machine, toState: string) {
    if (submitting) return;
    setError(null);
    setSubmitting(true);
    const idempotencyKey = crypto.randomUUID();
    try {
      const res = await fetch(
        `/api/orders/${encodeURIComponent(orderId)}/transitions/${encodeURIComponent(machine)}`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Idempotency-Key": idempotencyKey,
          },
          body: JSON.stringify({ toState, rowVersion }),
        },
      );
      if (!res.ok) {
        const errBody = await res.json().catch(() => ({ error: `${res.status}` }));
        throw new Error(errBody.error ?? `${res.status}`);
      }
      startTransition(() => router.refresh());
    } catch (err) {
      const message = err instanceof Error ? err.message : "unknown";
      if (message.includes("412")) {
        setError(tErrors("stale_version"));
      } else if (message.includes("409")) {
        setError(tErrors("illegal_transition"));
      } else {
        setError(message);
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="flex flex-col gap-ds-sm">
      <div className="flex flex-wrap gap-ds-sm">
        {decisions.map(({ machine, decision }, i) => {
          if (decision.kind === "hide") return null;
          const label = (() => {
            try {
              return t(decision.labelKey as never);
            } catch {
              return decision.labelKey;
            }
          })();
          if (decision.kind === "render") {
            return (
              <Button
                key={`${machine}-${decision.toState}-${i}`}
                type="button"
                disabled={submitting}
                onClick={() => fire(machine, decision.toState)}
              >
                {label}
              </Button>
            );
          }
          return (
            <Button
              key={`${machine}-disabled-${i}`}
              type="button"
              variant="outline"
              disabled
              title={decision.reason}
            >
              {label}
            </Button>
          );
        })}
      </div>
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}
