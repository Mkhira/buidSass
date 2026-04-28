/**
 * T040d: ExportJobStatus<TFilterSnapshot> (FR-025).
 *
 * Generic status widget for any async export job (017 ledger, 018
 * finance). Polls `GET /api/<scope>/exports/[jobId]` every 3s until
 * terminal status; renders queued / in_progress / done / failed.
 *
 * Generic over the filter snapshot shape so 017 + 018 reuse without
 * type erosion.
 */
"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { buttonVariants } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import type { ReactNode } from "react";

export type ExportJobStatusValue = "queued" | "in_progress" | "done" | "failed";

export interface ExportJob<TFilterSnapshot> {
  id: string;
  status: ExportJobStatusValue;
  progress?: number;
  rowCount?: number | null;
  downloadUrl?: string | null;
  filterSnapshot?: TFilterSnapshot;
  error?: { reasonCode: string; message?: string } | null;
  createdAt: string;
}

export interface ExportJobStatusProps<TFilterSnapshot> {
  /** Endpoint to poll, e.g. `/api/inventory/ledger/exports/${jobId}`. */
  pollUrl: string;
  /** Optional: render the filter snapshot read-only in a side card. */
  renderSnapshot?: (snapshot: TFilterSnapshot) => ReactNode;
  /** Called once when status flips to `done`. */
  onDone?: (job: ExportJob<TFilterSnapshot>) => void;
  /** Override the 3000ms polling cadence. */
  pollIntervalMs?: number;
}

export function ExportJobStatus<TFilterSnapshot>({
  pollUrl,
  renderSnapshot,
  onDone,
  pollIntervalMs = 3000,
}: ExportJobStatusProps<TFilterSnapshot>) {
  const t = useTranslations("shell.export_job");
  const [job, setJob] = useState<ExportJob<TFilterSnapshot> | null>(null);
  const onDoneRef = useRef(onDone);
  onDoneRef.current = onDone;

  useEffect(() => {
    let cancelled = false;
    let timeout: ReturnType<typeof setTimeout> | null = null;

    async function tick() {
      try {
        const res = await fetch(pollUrl, { cache: "no-store" });
        if (!res.ok) {
          if (!cancelled) {
            setJob({
              id: pollUrl,
              status: "failed",
              error: { reasonCode: `http_${res.status}` },
              createdAt: new Date().toISOString(),
            } as ExportJob<TFilterSnapshot>);
          }
          return;
        }
        const next = (await res.json()) as ExportJob<TFilterSnapshot>;
        if (cancelled) return;
        setJob(next);
        if (next.status === "done") {
          onDoneRef.current?.(next);
          return;
        }
        if (next.status === "failed") return;
        timeout = setTimeout(tick, pollIntervalMs);
      } catch {
        if (!cancelled) {
          timeout = setTimeout(tick, pollIntervalMs);
        }
      }
    }

    void tick();
    return () => {
      cancelled = true;
      if (timeout) clearTimeout(timeout);
    };
  }, [pollUrl, pollIntervalMs]);

  if (!job) {
    return <Skeleton className="h-24 w-full" />;
  }

  return (
    <div className="space-y-ds-md">
      <div className="flex items-center gap-ds-sm">
        <StatusBadge status={job.status} />
        {job.status === "in_progress" && job.progress !== undefined ? (
          <span className="text-sm text-muted-foreground" aria-live="polite">
            {t("in_progress", { progress: job.progress })}
          </span>
        ) : null}
        {job.status === "done" && job.downloadUrl ? (
          <a
            href={job.downloadUrl}
            download
            className={buttonVariants({ variant: "default", size: "sm" })}
          >
            {t("download")}
          </a>
        ) : null}
      </div>

      {job.status === "failed" ? (
        <Alert variant="destructive">
          <AlertDescription className="font-mono text-xs">
            {job.error?.reasonCode ?? "unknown"}
          </AlertDescription>
        </Alert>
      ) : null}

      {renderSnapshot && job.filterSnapshot ? (
        <div className="rounded-md border border-border bg-muted/30 p-ds-md">
          {renderSnapshot(job.filterSnapshot)}
        </div>
      ) : null}
    </div>
  );
}

function StatusBadge({ status }: { status: ExportJobStatusValue }) {
  const t = useTranslations("shell.export_job");
  const map: Record<ExportJobStatusValue, { label: string; variant: "default" | "secondary" | "destructive" | "outline" }> = {
    queued: { label: t("queued"), variant: "secondary" },
    in_progress: { label: t("in_progress", { progress: 0 }).replace(/\(.*\)/, "").trim(), variant: "default" },
    done: { label: t("done"), variant: "outline" },
    failed: { label: t("failed"), variant: "destructive" },
  };
  return <Badge variant={map[status].variant}>{map[status].label}</Badge>;
}
