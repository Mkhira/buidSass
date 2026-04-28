/**
 * T040: ErrorState — surfaced when a fetch / mutation fails recoverably.
 */
"use client";

import { useTranslations } from "next-intl";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { AlertTriangle } from "lucide-react";

interface ErrorStateProps {
  title?: string;
  detail?: string;
  /** Optional retry handler (omit to render an alert without action). */
  onRetry?: () => void;
  reasonCode?: string;
}

export function ErrorState({ title, detail, onRetry, reasonCode }: ErrorStateProps) {
  const t = useTranslations("shell");
  return (
    <Alert variant="destructive" role="alert" aria-live="assertive">
      <AlertTriangle aria-hidden="true" className="size-4" />
      <AlertTitle>{title ?? t("error")}</AlertTitle>
      {detail ? <AlertDescription>{detail}</AlertDescription> : null}
      {reasonCode ? (
        <AlertDescription className="font-mono text-xs opacity-70">
          {reasonCode}
        </AlertDescription>
      ) : null}
      {onRetry ? (
        <div className="mt-ds-sm">
          <Button onClick={onRetry} size="sm" variant="secondary">
            {t("export_job.failed")}
          </Button>
        </div>
      ) : null}
    </Alert>
  );
}
