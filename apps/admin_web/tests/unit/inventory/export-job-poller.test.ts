import { describe, expect, it } from "vitest";
import type { ExportJob } from "@/lib/api/clients/inventory";
import {
  isTerminal,
  pollUntilTerminal,
} from "@/lib/inventory/export-job-poller";

const baseJob: ExportJob = {
  id: "job",
  status: "queued",
  progress: 0,
  downloadUrl: null,
  error: null,
  createdAt: "2026-04-27T00:00:00Z",
};

describe("SM-4 export-job poller", () => {
  it("isTerminal recognises done + failed", () => {
    expect(isTerminal("done")).toBe(true);
    expect(isTerminal("failed")).toBe(true);
    expect(isTerminal("queued")).toBe(false);
    expect(isTerminal("in_progress")).toBe(false);
  });

  it("pollUntilTerminal abort signals throw", async () => {
    const ctrl = new AbortController();
    ctrl.abort();
    await expect(
      pollUntilTerminal("job", async () => baseJob, { signal: ctrl.signal }),
    ).rejects.toThrow();
  });
});
