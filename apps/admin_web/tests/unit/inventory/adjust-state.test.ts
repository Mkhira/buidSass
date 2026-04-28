import { describe, expect, it } from "vitest";
import {
  requiresNote,
  validateAdjustment,
  wouldBeBelowZero,
} from "@/lib/inventory/adjust-state";

describe("SM-1 adjustment submission", () => {
  it("requiresNote flags theft_loss / write_off_below_zero / breakage", () => {
    expect(requiresNote("theft_loss")).toBe(true);
    expect(requiresNote("write_off_below_zero")).toBe(true);
    expect(requiresNote("breakage")).toBe(true);
    expect(requiresNote("stock_count")).toBe(false);
    expect(requiresNote("receive")).toBe(false);
  });

  it("wouldBeBelowZero detects pre-validation", () => {
    expect(wouldBeBelowZero({ delta: -5, onHand: 3 })).toBe(true);
    expect(wouldBeBelowZero({ delta: -3, onHand: 3 })).toBe(false);
    expect(wouldBeBelowZero({ delta: 5, onHand: 0 })).toBe(false);
  });

  it("missing-note path: returns missing_note_blocked", () => {
    const next = validateAdjustment({
      delta: -1,
      reasonCode: "theft_loss",
      note: "short",
      onHand: 100,
      hasWriteoffBelowZeroPermission: true,
    });
    expect(next.kind).toBe("missing_note_blocked");
  });

  it("below-zero without permission: returns below_zero_blocked", () => {
    const next = validateAdjustment({
      delta: -10,
      reasonCode: "stock_count",
      note: "",
      onHand: 5,
      hasWriteoffBelowZeroPermission: false,
    });
    expect(next.kind).toBe("below_zero_blocked");
  });

  it("below-zero WITH permission proceeds to validating", () => {
    const next = validateAdjustment({
      delta: -10,
      reasonCode: "write_off_below_zero",
      note: "ten characters at least",
      onHand: 5,
      hasWriteoffBelowZeroPermission: true,
    });
    expect(next.kind).toBe("validating");
  });

  it("happy path: passes validation", () => {
    const next = validateAdjustment({
      delta: 5,
      reasonCode: "receive",
      note: "",
      onHand: 0,
      hasWriteoffBelowZeroPermission: false,
    });
    expect(next.kind).toBe("validating");
  });

  it("note exactly 10 chars passes the requirement", () => {
    const next = validateAdjustment({
      delta: -1,
      reasonCode: "breakage",
      note: "1234567890",
      onHand: 100,
      hasWriteoffBelowZeroPermission: true,
    });
    expect(next.kind).toBe("validating");
  });
});
