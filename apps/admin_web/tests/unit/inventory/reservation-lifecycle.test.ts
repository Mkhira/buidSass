import { describe, expect, it } from "vitest";
import type { Reservation } from "@/lib/api/clients/inventory";
import {
  reservationStateFor,
  ttlSecondsRemaining,
} from "@/lib/inventory/reservation-lifecycle";

function res(expiresAt: string): Reservation {
  return {
    id: "r",
    ownerKind: "cart",
    ownerId: "o",
    skuId: "s",
    warehouseId: "w",
    qty: 1,
    expiresAt,
    createdAt: "2026-04-26T00:00:00Z",
    actorKind: "system",
    actorId: null,
  };
}

describe("SM-3 reservation lifecycle", () => {
  const now = new Date("2026-04-27T12:00:00Z");

  it("active when expiresAt is in the future", () => {
    expect(reservationStateFor(res("2026-04-27T13:00:00Z"), now)).toBe("active");
  });

  it("expired when expiresAt has passed", () => {
    expect(reservationStateFor(res("2026-04-27T11:00:00Z"), now)).toBe(
      "expired",
    );
  });

  it("ttl floors at zero on expired", () => {
    expect(ttlSecondsRemaining(res("2026-04-27T10:00:00Z"), now)).toBe(0);
  });

  it("ttl is whole-second floor", () => {
    expect(ttlSecondsRemaining(res("2026-04-27T12:01:30Z"), now)).toBe(90);
  });
});
