/**
 * T076: en/ar key-set parity test.
 *
 * Asserts every key in `messages/en.json` exists in `messages/ar.json`
 * and vice versa. Missing translations would otherwise surface at
 * runtime as a `MISSING_MESSAGE` warning; this gate catches them at
 * PR time.
 *
 * Tolerated: the AR file may include `EN_PLACEHOLDER` markers (T075
 * manual gate). The CI build for AR fails when any marker remains —
 * that's a separate gate handled by the AR-build flow.
 */
import { describe, it, expect } from "vitest";
import en from "@/messages/en.json";
import ar from "@/messages/ar.json";

function flattenKeys(obj: unknown, prefix: string[] = [], out: string[] = []): string[] {
  if (obj && typeof obj === "object") {
    for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
      if (k.startsWith("@@")) continue; // tooling metadata (e.g. @@x-source)
      const next = [...prefix, k];
      if (v && typeof v === "object") flattenKeys(v, next, out);
      else out.push(next.join("."));
    }
  }
  return out;
}

describe("i18n key-set parity", () => {
  it("every key in en.json exists in ar.json", () => {
    const enKeys = new Set(flattenKeys(en));
    const arKeys = new Set(flattenKeys(ar));
    const missingInAr = [...enKeys].filter((k) => !arKeys.has(k));
    expect(missingInAr, `missing in ar.json:\n${missingInAr.join("\n")}`).toEqual([]);
  });

  it("every key in ar.json exists in en.json", () => {
    const enKeys = new Set(flattenKeys(en));
    const arKeys = new Set(flattenKeys(ar));
    const missingInEn = [...arKeys].filter((k) => !enKeys.has(k));
    expect(missingInEn, `missing in en.json:\n${missingInEn.join("\n")}`).toEqual([]);
  });
});
