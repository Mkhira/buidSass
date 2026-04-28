/**
 * T020 — product state pill (`draft` / `scheduled@<time>` / `published`).
 */
"use client";

import { useTranslations } from "next-intl";
import { useFormatter } from "next-intl";
import type { ClientProductState } from "@/lib/catalog/product-state";
import { pillFor } from "@/lib/catalog/product-state";
import { cn } from "@/lib/utils";

const TONE_CLASSES: Record<
  "neutral" | "info" | "success" | "warning",
  string
> = {
  neutral: "bg-muted text-muted-foreground",
  info: "bg-blue-100 text-blue-900 dark:bg-blue-950 dark:text-blue-100",
  success: "bg-green-100 text-green-900 dark:bg-green-950 dark:text-green-100",
  warning:
    "bg-yellow-100 text-yellow-900 dark:bg-yellow-950 dark:text-yellow-100",
};

export interface ProductStatePillProps {
  state: ClientProductState;
  scheduledPublishAt?: string | null;
}

export function ProductStatePill({
  state,
  scheduledPublishAt,
}: ProductStatePillProps) {
  const t = useTranslations("catalog.product.state");
  const fmt = useFormatter();
  const { labelKey, tone } = pillFor(state);
  const messageKey = labelKey.split(".").pop()!;
  const label = t(messageKey);
  const showSchedule = state === "scheduled" && scheduledPublishAt;
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium",
        TONE_CLASSES[tone],
      )}
    >
      {label}
      {showSchedule
        ? ` · ${fmt.dateTime(new Date(scheduledPublishAt), { dateStyle: "short", timeStyle: "short" })}`
        : null}
    </span>
  );
}
