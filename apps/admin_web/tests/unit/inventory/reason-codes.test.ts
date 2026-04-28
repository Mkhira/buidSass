import { describe, expect, it } from "vitest";
import { requiresNote } from "@/lib/inventory/reason-codes";

describe("reason codes", () => {
  it("flags the three mandatory-note codes", () => {
    expect(requiresNote("theft_loss")).toBe(true);
    expect(requiresNote("write_off_below_zero")).toBe(true);
    expect(requiresNote("breakage")).toBe(true);
  });

  it("does not flag stock_count, receive, return_inbound, etc", () => {
    expect(requiresNote("stock_count")).toBe(false);
    expect(requiresNote("receive")).toBe(false);
    expect(requiresNote("return_inbound")).toBe(false);
    expect(requiresNote("write_off_expiry")).toBe(false);
  });
});
