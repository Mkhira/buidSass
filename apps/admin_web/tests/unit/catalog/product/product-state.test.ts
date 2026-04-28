/**
 * Unit tests for SM-1 (product state) — covers every documented transition
 * in `data-model.md` plus the pill helper.
 */
import { describe, expect, it } from "vitest";
import {
  allowedTransitions,
  canTransition,
  pillFor,
} from "@/lib/catalog/product-state";

describe("SM-1 product state", () => {
  it("draft permits publish_now and schedule_publish", () => {
    const acts = allowedTransitions("draft").map((t) => t.actionKey);
    expect(acts).toContain("publish_now");
    expect(acts).toContain("schedule_publish");
  });

  it("scheduled permits publish_now and unschedule", () => {
    const acts = allowedTransitions("scheduled").map((t) => t.actionKey);
    expect(acts).toContain("publish_now");
    expect(acts).toContain("unschedule");
  });

  it("published permits revert_to_draft only", () => {
    const acts = allowedTransitions("published").map((t) => t.actionKey);
    expect(acts).toEqual(["revert_to_draft"]);
  });

  it("discarded is terminal", () => {
    expect(allowedTransitions("discarded")).toEqual([]);
  });

  it("canTransition gates draft -> published", () => {
    expect(canTransition("draft", "published")).toBe(true);
    expect(canTransition("published", "scheduled")).toBe(false);
  });

  it("pillFor maps every state to a tone + label", () => {
    expect(pillFor("draft").tone).toBe("neutral");
    expect(pillFor("scheduled").tone).toBe("info");
    expect(pillFor("published").tone).toBe("success");
    expect(pillFor("discarded").tone).toBe("warning");
  });
});
