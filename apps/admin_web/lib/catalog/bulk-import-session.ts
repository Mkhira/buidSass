/**
 * T007 — SM-2 (bulk-import session).
 *
 * States: uploaded → validating → validated → committing → committed (terminal)
 *                                                       └─ failed     (terminal)
 *
 * The wizard's three steps consume this machine to gate the commit
 * button (only enabled in `validated`) and surface progress.
 */
import type { BulkImportSession } from "@/lib/api/clients/catalog";

export type BulkImportStatus = BulkImportSession["status"];

export interface BulkImportTransition {
  from: BulkImportStatus;
  to: BulkImportStatus;
  trigger: "server" | "user_commit" | "user_abandon";
}

export const BULK_IMPORT_TRANSITIONS: BulkImportTransition[] = [
  { from: "uploaded", to: "validating", trigger: "server" },
  { from: "validating", to: "validated", trigger: "server" },
  { from: "validating", to: "failed", trigger: "server" },
  { from: "validated", to: "committing", trigger: "user_commit" },
  { from: "committing", to: "committed", trigger: "server" },
  { from: "committing", to: "failed", trigger: "server" },
];

const TERMINAL: ReadonlySet<BulkImportStatus> = new Set(["committed", "failed"]);

export function isTerminal(status: BulkImportStatus): boolean {
  return TERMINAL.has(status);
}

export function canCommit(session: BulkImportSession): boolean {
  return (
    session.status === "validated" &&
    session.erroredRowCount === 0 &&
    session.validatedRowCount > 0
  );
}

export function progressStepFor(status: BulkImportStatus): 1 | 2 | 3 {
  switch (status) {
    case "uploaded":
    case "validating":
      return 1;
    case "validated":
      return 2;
    case "committing":
    case "committed":
    case "failed":
      return 3;
  }
}
