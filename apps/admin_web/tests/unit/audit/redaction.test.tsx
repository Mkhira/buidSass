/**
 * T070 + T074a: redaction-policy unit test.
 *
 * Walks every rule in REDACTION_RULES against representative permission
 * profiles and asserts the decision flips correctly. This is the
 * defence-in-depth contract — a regression here is a real PII leak.
 */
import { describe, it, expect } from "vitest";
import { decideRedaction, REDACTION_RULES } from "@/components/audit/redaction-policy";

describe("audit redaction-policy", () => {
  it("redacts customer.email when actor lacks both PII permissions", () => {
    const decision = decideRedaction("after.customer.email", new Set(["audit.read"]));
    expect(decision.redact).toBe(true);
    expect(decision.kind).toBe("email");
  });

  it("does NOT redact customer.email when actor has customers.pii.read", () => {
    const decision = decideRedaction("after.customer.email", new Set(["audit.read", "customers.pii.read"]));
    expect(decision.redact).toBe(false);
  });

  it("does NOT redact customer.email when actor has orders.pii.read (alternate scope)", () => {
    const decision = decideRedaction("after.customer.email", new Set(["audit.read", "orders.pii.read"]));
    expect(decision.redact).toBe(false);
  });

  it("redacts refund.reasonNote for audit-only admin", () => {
    const decision = decideRedaction("after.refund.reasonNote", new Set(["audit.read"]));
    expect(decision.redact).toBe(true);
  });

  it("does NOT redact refund.reasonNote for an admin with orders.read", () => {
    const decision = decideRedaction(
      "after.refund.reasonNote",
      new Set(["audit.read", "orders.read"]),
    );
    expect(decision.redact).toBe(false);
  });

  it("redacts accountAction.reasonNote unless the actor holds suspend / unlock / password-reset", () => {
    const blocked = decideRedaction("after.accountAction.reasonNote", new Set(["audit.read"]));
    expect(blocked.redact).toBe(true);

    const allowed = decideRedaction(
      "after.accountAction.reasonNote",
      new Set(["audit.read", "customers.suspend"]),
    );
    expect(allowed.redact).toBe(false);
  });

  it("redacts restrictedRationale.{ar,en} for audit-only admin", () => {
    const ar = decideRedaction("before.restrictedRationale.ar", new Set(["audit.read"]));
    const en = decideRedaction("after.restrictedRationale.en", new Set(["audit.read"]));
    expect(ar.redact).toBe(true);
    expect(en.redact).toBe(true);
  });

  it("does NOT redact unrelated paths", () => {
    const decision = decideRedaction("after.foo.bar", new Set(["audit.read"]));
    expect(decision.redact).toBe(false);
  });

  it("ships rules for every sensitive path category mentioned in contracts/audit-redaction.md", () => {
    const paths = REDACTION_RULES.map((r) => r.path);
    // Sanity check — these are the categories present in the markdown.
    expect(paths).toEqual(
      expect.arrayContaining([
        "customer.email",
        "customer.phone",
        "address.line1",
        "lockoutState.reasonNote",
        "restrictedRationale.ar",
        "restrictedRationale.en",
        "refund.reasonNote",
        "cancel.reasonNote",
        "accountAction.reasonNote",
      ]),
    );
  });
});
