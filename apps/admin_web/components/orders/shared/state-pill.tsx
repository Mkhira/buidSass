/**
 * T017 — single state-pill primitive consumed by list (4×) + detail (4×).
 */
"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils";

export type StateMachine = "order" | "payment" | "fulfillment" | "refund";

const TONE_CLASSES: Record<
  "neutral" | "info" | "success" | "warning" | "danger",
  string
> = {
  neutral: "bg-muted text-muted-foreground",
  info: "bg-blue-100 text-blue-900 dark:bg-blue-950 dark:text-blue-100",
  success: "bg-green-100 text-green-900 dark:bg-green-950 dark:text-green-100",
  warning: "bg-yellow-100 text-yellow-900 dark:bg-yellow-950 dark:text-yellow-100",
  danger: "bg-red-100 text-red-900 dark:bg-red-950 dark:text-red-100",
};

function toneFor(machine: StateMachine, state: string): keyof typeof TONE_CLASSES {
  switch (machine) {
    case "order":
      if (state === "cancelled") return "danger";
      if (state === "completed" || state === "delivered") return "success";
      return "info";
    case "payment":
      if (state === "captured") return "success";
      if (state === "refunded") return "warning";
      return "info";
    case "fulfillment":
      if (state === "delivered") return "success";
      if (state === "packed" || state === "handed_to_carrier") return "info";
      return "neutral";
    case "refund":
      if (state === "full") return "warning";
      if (state === "partial") return "info";
      return "neutral";
  }
}

export interface StatePillProps {
  machine: StateMachine;
  state: string;
}

export function StatePill({ machine, state }: StatePillProps) {
  const t = useTranslations("orders.states");
  let label: string;
  try {
    label = t(state as never);
  } catch {
    label = state;
  }
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
        TONE_CLASSES[toneFor(machine, state)],
      )}
    >
      {label}
    </span>
  );
}
