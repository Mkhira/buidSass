import { describe, expect, it } from "vitest";
import type { LineItem } from "@/lib/api/clients/orders";
import {
  checkOverRefund,
  reasonNoteValid,
  requiresStepUp,
  validateRefundDraft,
} from "@/lib/orders/refund-state";

function line(partial: Partial<LineItem> = {}): LineItem {
  return {
    id: partial.id ?? "L1",
    productId: "p",
    sku: "s",
    name: { en: "Item", ar: "Item" },
    qty: partial.qty ?? 5,
    deliveredQty: partial.deliveredQty ?? 5,
    alreadyRefundedQty: partial.alreadyRefundedQty ?? 0,
    unitPriceMinor: 1000,
    lineSubtotalMinor: 5000,
  };
}

describe("SM-1 refund-state", () => {
  it("rejects too-short reason note", () => {
    expect(reasonNoteValid("short")).toBe(false);
    expect(reasonNoteValid("1234567890")).toBe(true);
    expect(reasonNoteValid("")).toBe(false);
  });

  it("checkOverRefund flags lines exceeding deliveredQty - alreadyRefundedQty", () => {
    const result = checkOverRefund({
      lines: [{ lineId: "L1", qty: 6, amountMinor: 0 }],
      reasonNote: "valid reason ten chars",
      lineItems: [line({ id: "L1", deliveredQty: 5, alreadyRefundedQty: 0 })],
      refundAmountMinor: 0,
      grandTotalMinor: 0,
      stepUpThresholdMinor: 100000,
    });
    expect(result?.kind).toBe("over_refund_blocked");
  });

  it("checkOverRefund returns null when all lines within cap", () => {
    const result = checkOverRefund({
      lines: [{ lineId: "L1", qty: 3, amountMinor: 0 }],
      reasonNote: "valid reason ten chars",
      lineItems: [line({ id: "L1", deliveredQty: 5, alreadyRefundedQty: 1 })],
      refundAmountMinor: 0,
      grandTotalMinor: 0,
      stepUpThresholdMinor: 100000,
    });
    expect(result).toBeNull();
  });

  it("requiresStepUp triggers on full grand total", () => {
    expect(
      requiresStepUp({
        refundAmountMinor: 50000,
        grandTotalMinor: 50000,
        stepUpThresholdMinor: 999999,
      }),
    ).toBe(true);
  });

  it("requiresStepUp triggers above per-market threshold", () => {
    expect(
      requiresStepUp({
        refundAmountMinor: 100001,
        grandTotalMinor: 500000,
        stepUpThresholdMinor: 100000,
      }),
    ).toBe(true);
  });

  it("requiresStepUp false on small partial below threshold", () => {
    expect(
      requiresStepUp({
        refundAmountMinor: 5000,
        grandTotalMinor: 500000,
        stepUpThresholdMinor: 100000,
      }),
    ).toBe(false);
  });

  it("validateRefundDraft happy path", () => {
    const result = validateRefundDraft({
      lines: [{ lineId: "L1", qty: 1, amountMinor: 1000 }],
      reasonNote: "valid reason text here",
      lineItems: [line({ id: "L1", deliveredQty: 5, alreadyRefundedQty: 0 })],
      refundAmountMinor: 1000,
      grandTotalMinor: 5000,
      stepUpThresholdMinor: 100000,
    });
    expect(result).toBeNull();
  });

  it("validateRefundDraft surfaces missing reason first", () => {
    const result = validateRefundDraft({
      lines: [{ lineId: "L1", qty: 100, amountMinor: 0 }],
      reasonNote: "x",
      lineItems: [line({ id: "L1", deliveredQty: 5, alreadyRefundedQty: 0 })],
      refundAmountMinor: 0,
      grandTotalMinor: 0,
      stepUpThresholdMinor: 100000,
    });
    expect(result?.kind).toBe("missing_reason_blocked");
  });
});
