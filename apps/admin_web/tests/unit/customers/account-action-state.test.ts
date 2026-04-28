import { describe, expect, it } from "vitest";
import {
  isSelfAction,
  preSubmitCheck,
  reasonNoteValid,
} from "@/lib/customers/account-action-state";

describe("SM-1 account-action-state", () => {
  it("reasonNoteValid enforces 10..2000 chars", () => {
    expect(reasonNoteValid("")).toBe(false);
    expect(reasonNoteValid("9chars000")).toBe(false);
    expect(reasonNoteValid("ten chars!")).toBe(true);
    expect(reasonNoteValid("a".repeat(2001))).toBe(false);
    expect(reasonNoteValid("a".repeat(2000))).toBe(true);
  });

  it("isSelfAction true when ids match", () => {
    expect(
      isSelfAction({
        targetCustomerId: "u1",
        currentSessionAdminId: "u1",
      }),
    ).toBe(true);
    expect(
      isSelfAction({
        targetCustomerId: "u1",
        currentSessionAdminId: "u2",
      }),
    ).toBe(false);
  });

  it("preSubmitCheck blocks self-action with terminal failure", () => {
    const next = preSubmitCheck({
      action: "suspend",
      reasonNote: "valid reason ten chars",
      targetCustomerId: "u1",
      currentSessionAdminId: "u1",
    });
    expect(next?.kind).toBe("failed_terminal");
  });

  it("preSubmitCheck stays in confirming on missing reason", () => {
    const next = preSubmitCheck({
      action: "suspend",
      reasonNote: "x",
      targetCustomerId: "u1",
      currentSessionAdminId: "u2",
    });
    expect(next?.kind).toBe("confirming");
  });

  it("preSubmitCheck returns null on valid input", () => {
    const next = preSubmitCheck({
      action: "suspend",
      reasonNote: "valid reason ten chars",
      targetCustomerId: "u1",
      currentSessionAdminId: "u2",
    });
    expect(next).toBeNull();
  });
});
