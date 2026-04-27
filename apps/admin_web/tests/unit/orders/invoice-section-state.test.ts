import { describe, expect, it } from "vitest";
import { projectInvoiceSection } from "@/lib/orders/invoice-section-state";

describe("SM-2 invoice-section-state", () => {
  it("pending → pending kind", () => {
    const result = projectInvoiceSection({
      status: "pending",
      downloadUrl: null,
      errorReasonCode: null,
      lastChangedAt: "2026-04-27T00:00:00Z",
      canRegenerate: true,
    });
    expect(result.kind).toBe("pending");
  });

  it("available with downloadUrl → available", () => {
    const result = projectInvoiceSection({
      status: "available",
      downloadUrl: "https://example.com/invoice.pdf",
      errorReasonCode: null,
      lastChangedAt: "2026-04-27T00:00:00Z",
      canRegenerate: true,
    });
    expect(result.kind).toBe("available");
  });

  it("available with missing downloadUrl falls to failed", () => {
    const result = projectInvoiceSection({
      status: "available",
      downloadUrl: null,
      errorReasonCode: null,
      lastChangedAt: "2026-04-27T00:00:00Z",
      canRegenerate: true,
    });
    expect(result.kind).toBe("failed");
    expect(result).toMatchObject({ errorReasonCode: "missing_download_url" });
  });

  it("failed surfaces canRegenerate", () => {
    const result = projectInvoiceSection({
      status: "failed",
      downloadUrl: null,
      errorReasonCode: "vat_validation_failed",
      lastChangedAt: "2026-04-27T00:00:00Z",
      canRegenerate: false,
    });
    expect(result.kind).toBe("failed");
    expect(result).toMatchObject({
      errorReasonCode: "vat_validation_failed",
      canRegenerate: false,
    });
  });
});
