import { describe, expect, it } from "vitest";
import { modeFor } from "@/lib/customers/history-panel-mode";

describe("SM-2 history-panel-mode", () => {
  it("flag true → shipped", () => {
    expect(modeFor(true)).toBe("shipped");
  });
  it("flag false → placeholder", () => {
    expect(modeFor(false)).toBe("placeholder");
  });
});
