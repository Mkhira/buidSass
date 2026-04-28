/**
 * T011 — SM-2 (invoice section).
 *
 * Mirrors spec 012's invoice status. The detail page invoice section
 * renders Pending (with last-changed-at), Available (download +
 * regenerate buttons), or Failed (regenerate + reason).
 */

export type InvoiceSectionState =
  | { kind: "pending"; lastChangedAt: string }
  | { kind: "available"; downloadUrl: string; lastChangedAt: string }
  | {
      kind: "failed";
      errorReasonCode: string;
      lastChangedAt: string;
      canRegenerate: boolean;
    };

export interface InvoiceSectionInput {
  status: "pending" | "available" | "failed";
  downloadUrl: string | null;
  errorReasonCode: string | null;
  lastChangedAt: string;
  /** Whether the admin has invoices.regenerate permission. */
  canRegenerate: boolean;
}

export function projectInvoiceSection(
  input: InvoiceSectionInput,
): InvoiceSectionState {
  switch (input.status) {
    case "pending":
      return { kind: "pending", lastChangedAt: input.lastChangedAt };
    case "available":
      if (!input.downloadUrl) {
        return {
          kind: "failed",
          errorReasonCode: "missing_download_url",
          lastChangedAt: input.lastChangedAt,
          canRegenerate: input.canRegenerate,
        };
      }
      return {
        kind: "available",
        downloadUrl: input.downloadUrl,
        lastChangedAt: input.lastChangedAt,
      };
    case "failed":
      return {
        kind: "failed",
        errorReasonCode: input.errorReasonCode ?? "unknown",
        lastChangedAt: input.lastChangedAt,
        canRegenerate: input.canRegenerate,
      };
  }
}
