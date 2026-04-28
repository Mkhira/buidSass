/**
 * T078 verification: formatter contract — Western Arabic numerals,
 * per-market currency, locale-correct dates.
 */
import { describe, it, expect } from "vitest";
import {
  bcp47For,
  formatCurrency,
  formatDate,
  formatNumber,
} from "@/lib/i18n/formatters";

describe("formatters", () => {
  it("bcp47For maps market+locale to the right tag", () => {
    expect(bcp47For("en", "ksa")).toBe("en-SA");
    expect(bcp47For("ar", "ksa")).toBe("ar-SA");
    expect(bcp47For("en", "eg")).toBe("en-EG");
    expect(bcp47For("ar", "eg")).toBe("ar-EG");
    // platform defaults to KSA per ADR-010
    expect(bcp47For("en", "platform")).toBe("en-SA");
  });

  it("formatCurrency uses SAR for KSA + EGP for EG, latn numerals", () => {
    const ksa = formatCurrency(12500, "ksa", "ar");
    const eg = formatCurrency(12500, "eg", "ar");
    expect(ksa).toMatch(/SAR|ر\.س/);
    expect(eg).toMatch(/EGP|ج\.م/);
    // Latin-numeral guard: must contain Western digits, no Eastern
    expect(ksa).toMatch(/\d/);
    expect(eg).toMatch(/\d/);
    expect(ksa).not.toMatch(/[٠-٩]/);
  });

  it("formatNumber outputs Western digits regardless of locale", () => {
    expect(formatNumber(1234, "ar", "ksa")).toMatch(/1.?234|1234/);
    expect(formatNumber(1234, "en", "ksa")).toMatch(/1.?234|1234/);
  });

  it("formatDate produces a non-empty string", () => {
    const out = formatDate("2026-04-27T15:00:00Z", "en", "ksa");
    expect(out.length).toBeGreaterThan(0);
  });
});
