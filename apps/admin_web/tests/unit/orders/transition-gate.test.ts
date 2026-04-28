import { describe, expect, it } from "vitest";
import {
  candidateTransitions,
  evaluateTransition,
  TRANSITIONS,
} from "@/lib/orders/transition-gate";

describe("transition gate", () => {
  it("hides when no rule matches the (machine, from, to) triple", () => {
    const decision = evaluateTransition({
      machine: "order",
      fromState: "placed",
      toState: "delivered", // wrong machine for delivered
      permissions: new Set(["orders.transition.order"]),
    });
    expect(decision.kind).toBe("hide");
    expect(decision).toMatchObject({ reason: "state_machine_disallowed" });
  });

  it("hides when permission missing", () => {
    const decision = evaluateTransition({
      machine: "order",
      fromState: "placed",
      toState: "confirmed",
      permissions: new Set(),
    });
    expect(decision.kind).toBe("hide");
    expect(decision).toMatchObject({ reason: "permission_missing" });
  });

  it("renders when rule matches and permission held", () => {
    const decision = evaluateTransition({
      machine: "fulfillment",
      fromState: "packed",
      toState: "handed_to_carrier",
      permissions: new Set(["orders.transition.fulfillment"]),
    });
    expect(decision.kind).toBe("render");
    expect(decision).toMatchObject({ actionKey: "hand_to_carrier" });
  });

  it("render_disabled when order closed", () => {
    const decision = evaluateTransition({
      machine: "fulfillment",
      fromState: "pending",
      toState: "packed",
      permissions: new Set(["orders.transition.fulfillment"]),
      orderClosed: true,
    });
    expect(decision.kind).toBe("render_disabled");
    expect(decision).toMatchObject({ reason: "order_closed" });
  });

  it("candidateTransitions enumerates from a given state", () => {
    const list = candidateTransitions({
      machine: "fulfillment",
      fromState: "pending",
      permissions: new Set(["orders.transition.fulfillment"]),
    });
    expect(list.length).toBeGreaterThan(0);
    expect(list.every((d) => d.kind === "render")).toBe(true);
  });

  it("every TRANSITIONS rule has unique (machine, from, to)", () => {
    const seen = new Set<string>();
    for (const r of TRANSITIONS) {
      const key = `${r.machine}|${r.fromState}|${r.toState}`;
      expect(seen.has(key), `duplicate rule ${key}`).toBe(false);
      seen.add(key);
    }
  });
});
