import { describe, expect, it } from "vitest";
import {
  BULK_IMPORT_TRANSITIONS,
  canCommit,
  isTerminal,
  progressStepFor,
} from "@/lib/catalog/bulk-import-session";

const baseSession = {
  id: "s",
  uploadedRowCount: 10,
  validatedRowCount: 10,
  erroredRowCount: 0,
  validationReportUrl: null,
  submittedBy: "u",
  createdAt: "2026-04-27T00:00:00Z",
};

describe("SM-2 bulk-import session", () => {
  it("isTerminal recognises committed and failed", () => {
    expect(isTerminal("committed")).toBe(true);
    expect(isTerminal("failed")).toBe(true);
    expect(isTerminal("validated")).toBe(false);
  });

  it("canCommit only when validated + zero errors + non-zero rows", () => {
    expect(canCommit({ ...baseSession, status: "validated" })).toBe(true);
    expect(
      canCommit({ ...baseSession, status: "validated", erroredRowCount: 1 }),
    ).toBe(false);
    expect(canCommit({ ...baseSession, status: "uploaded" })).toBe(false);
    expect(
      canCommit({ ...baseSession, status: "validated", validatedRowCount: 0 }),
    ).toBe(false);
  });

  it("progressStepFor maps statuses to wizard steps", () => {
    expect(progressStepFor("uploaded")).toBe(1);
    expect(progressStepFor("validating")).toBe(1);
    expect(progressStepFor("validated")).toBe(2);
    expect(progressStepFor("committing")).toBe(3);
    expect(progressStepFor("committed")).toBe(3);
    expect(progressStepFor("failed")).toBe(3);
  });

  it("documented transitions cover the lifecycle", () => {
    const fromStates = new Set(BULK_IMPORT_TRANSITIONS.map((t) => t.from));
    expect(fromStates).toContain("uploaded");
    expect(fromStates).toContain("validating");
    expect(fromStates).toContain("validated");
    expect(fromStates).toContain("committing");
  });
});
