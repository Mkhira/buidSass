/**
 * T009 — SM-4 (export-job lifecycle).
 *
 * Polls every 3 s until the job reaches a terminal state. Caller passes
 * the fetch function; the poller handles cancellation when the consuming
 * component unmounts.
 */
import type { ExportJob } from "@/lib/api/clients/inventory";

export const POLL_INTERVAL_MS = 3000;

export function isTerminal(status: ExportJob["status"]): boolean {
  return status === "done" || status === "failed";
}

export interface PollOptions {
  signal?: AbortSignal;
  onUpdate?: (job: ExportJob) => void;
}

/**
 * Polls a job by id until it reaches a terminal state OR the signal
 * aborts. Returns the final job. Throws on abort.
 */
export async function pollUntilTerminal(
  jobId: string,
  fetcher: (id: string) => Promise<ExportJob>,
  { signal, onUpdate }: PollOptions = {},
): Promise<ExportJob> {
  while (true) {
    if (signal?.aborted) {
      throw new DOMException("aborted", "AbortError");
    }
    const job = await fetcher(jobId);
    onUpdate?.(job);
    if (isTerminal(job.status)) return job;
    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(resolve, POLL_INTERVAL_MS);
      signal?.addEventListener(
        "abort",
        () => {
          clearTimeout(timer);
          reject(new DOMException("aborted", "AbortError"));
        },
        { once: true },
      );
    });
  }
}
