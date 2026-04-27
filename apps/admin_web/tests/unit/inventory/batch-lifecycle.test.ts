import { describe, expect, it } from "vitest";
import type { Batch } from "@/lib/api/clients/inventory";
import {
  batchStateFor,
  bucketBatchesIntoLanes,
  DEFAULT_NEAR_EXPIRY_THRESHOLD_DAYS,
} from "@/lib/inventory/batch-lifecycle";

const now = new Date("2026-04-27T00:00:00Z");

function batch(partial: Partial<Batch>): Batch {
  return {
    id: partial.id ?? "b",
    skuId: partial.skuId ?? "s",
    warehouseId: partial.warehouseId ?? "w",
    lotNumber: partial.lotNumber ?? "L1",
    supplierReference: partial.supplierReference ?? null,
    manufacturedOn: partial.manufacturedOn ?? "2026-01-01",
    expiresOn: partial.expiresOn ?? "2026-12-31",
    onHand: partial.onHand ?? 10,
    coaDocumentId: partial.coaDocumentId ?? null,
    receiptId: partial.receiptId ?? null,
    receiptReversed: partial.receiptReversed ?? false,
    rowVersion: partial.rowVersion ?? 0,
  };
}

describe("SM-2 batch lifecycle", () => {
  it("active batch with on-hand and far-future expiry", () => {
    expect(batchStateFor(batch({ expiresOn: "2027-12-31" }), now)).toBe("active");
  });

  it("near_expiry within threshold", () => {
    expect(batchStateFor(batch({ expiresOn: "2026-05-15" }), now)).toBe(
      "near_expiry",
    );
  });

  it("expired when expiresOn < now", () => {
    expect(batchStateFor(batch({ expiresOn: "2026-04-01" }), now)).toBe(
      "expired",
    );
  });

  it("written_off when on-hand <= 0", () => {
    expect(batchStateFor(batch({ onHand: 0 }), now)).toBe("written_off");
  });

  it("threshold override changes the boundary", () => {
    expect(
      batchStateFor(batch({ expiresOn: "2026-06-25" }), now, 60),
    ).toBe("near_expiry");
    expect(
      batchStateFor(
        batch({ expiresOn: "2026-06-25" }),
        now,
        DEFAULT_NEAR_EXPIRY_THRESHOLD_DAYS,
      ),
    ).toBe("active");
  });

  it("bucketBatchesIntoLanes drops written-off batches", () => {
    const lanes = bucketBatchesIntoLanes(
      [
        batch({ id: "a", expiresOn: "2026-05-10" }), // near
        batch({ id: "b", expiresOn: "2026-04-01" }), // expired
        batch({ id: "c", expiresOn: "2027-01-01" }), // future
        batch({ id: "d", onHand: 0 }), // written_off — dropped
      ],
      now,
    );
    const ids = lanes.flatMap((l) => l.batches.map((b) => b.id));
    expect(ids).toEqual(expect.arrayContaining(["a", "b", "c"]));
    expect(ids).not.toContain("d");
  });
});
