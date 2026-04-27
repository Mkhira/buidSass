/**
 * T040: EmptyState — surfaced when a list / detail returns no rows.
 */
import { useTranslations } from "next-intl";
import type { ReactNode } from "react";

interface EmptyStateProps {
  /** Localized title; falls back to `shell.empty`. */
  title?: string;
  /** Optional supporting copy. */
  body?: string;
  /** Optional CTA (e.g., "Clear filters"). */
  action?: ReactNode;
  /** Optional icon (lucide-react). */
  icon?: ReactNode;
}

export function EmptyState({ title, body, action, icon }: EmptyStateProps) {
  const t = useTranslations("shell");
  return (
    <div
      role="status"
      className="flex flex-col items-center justify-center gap-ds-sm rounded-md border border-dashed border-border bg-muted/30 p-ds-xl text-center"
    >
      {icon ? <div aria-hidden="true">{icon}</div> : null}
      <p className="text-sm font-medium text-foreground">{title ?? t("empty")}</p>
      {body ? <p className="text-sm text-muted-foreground">{body}</p> : null}
      {action ? <div className="mt-ds-xs">{action}</div> : null}
    </div>
  );
}
