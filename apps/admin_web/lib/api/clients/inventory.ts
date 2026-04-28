/**
 * T005 — spec 008 inventory client.
 *
 * Hand-typed mirrors of `data-model.md` until `pnpm gen:api` against
 * `services/backend_api/openapi.inventory.json` lands generated types
 * under `lib/api/types/inventory.ts`.
 */
import { proxyFetch } from "@/lib/api/proxy";

// ---- Shared shapes -----------------------------------------------------

export interface StockSnapshot {
  skuId: string;
  warehouseId: string;
  available: number;
  onHand: number;
  reserved: number;
  rowVersion: number;
}

export interface AdjustmentPayload {
  warehouseId: string;
  skuId: string;
  delta: number;
  reasonCode: string;
  batchId?: string | null;
  note?: string;
  rowVersion: number;
}

export interface AdjustmentResult {
  ledgerEntryId: string;
  snapshot: StockSnapshot;
}

export interface ReasonCode {
  code: string;
  labelKey: string;
  /** Server hint — UI also enforces locally via `requiresNote()`. */
  noteRequired: boolean;
  /** Server hint — UI also enforces via permissions. */
  belowZeroOnly?: boolean;
}

export interface LowStockRow {
  skuId: string;
  name: { en: string; ar: string };
  warehouseId: string;
  available: number;
  threshold: number;
  velocity7d: number;
  velocity30d: number;
  velocity90d: number;
  nearExpiry: boolean;
}

export interface BatchPayload {
  skuId: string;
  warehouseId: string;
  lotNumber: string;
  supplierReference?: string | null;
  manufacturedOn: string;
  expiresOn: string;
  onHand: number;
  coaDocumentId?: string | null;
}

export interface Batch extends BatchPayload {
  id: string;
  receiptId: string | null;
  receiptReversed: boolean;
  rowVersion: number;
}

export interface Reservation {
  id: string;
  ownerKind: "cart" | "order" | "quote";
  ownerId: string;
  skuId: string;
  warehouseId: string;
  qty: number;
  expiresAt: string;
  createdAt: string;
  actorKind: "system" | "admin";
  actorId: string | null;
}

export interface LedgerRow {
  id: string;
  skuId: string;
  warehouseId: string;
  delta: number;
  reasonCode: string;
  source:
    | "manual"
    | "reservation_convert"
    | "receipt"
    | "return"
    | "write_off"
    | "system";
  batchId: string | null;
  actor: { kind: string; id: string; displayName?: string };
  occurredAt: string;
  auditPermalink: string;
}

export interface LedgerPage {
  rows: LedgerRow[];
  nextCursor: string | null;
}

export interface ExportJob {
  id: string;
  status: "queued" | "in_progress" | "done" | "failed";
  progress?: number;
  downloadUrl: string | null;
  error: { reasonCode: string; message: string } | null;
  createdAt: string;
}

export interface Warehouse {
  id: string;
  code: string;
  name: { en: string; ar: string };
  marketCode: "ksa" | "eg";
  nearExpiryThresholdDays: number | null;
}

// ---- Endpoints ---------------------------------------------------------

export const inventoryApi = {
  warehouses: {
    list: () =>
      proxyFetch<Warehouse[]>("/v1/admin/inventory/warehouses"),
  },
  stock: {
    snapshot: (skuId: string, warehouseId: string) =>
      proxyFetch<StockSnapshot>(
        `/v1/admin/inventory/stock/${encodeURIComponent(skuId)}` +
          `?warehouseId=${encodeURIComponent(warehouseId)}`,
      ),
    listBySku: (filter: { search?: string; warehouseId?: string; cursor?: string }) => {
      const qs = new URLSearchParams();
      for (const [k, v] of Object.entries(filter)) {
        if (v !== undefined && v !== null && v !== "") qs.set(k, String(v));
      }
      const suffix = qs.toString() ? `?${qs.toString()}` : "";
      return proxyFetch<{ rows: StockSnapshot[]; nextCursor: string | null }>(
        `/v1/admin/inventory/stock${suffix}`,
      );
    },
  },
  adjustments: {
    create: (payload: AdjustmentPayload, idempotencyKey: string) =>
      proxyFetch<AdjustmentResult>("/v1/admin/inventory/adjustments", {
        method: "POST",
        body: JSON.stringify(payload),
        idempotencyKey,
      }),
  },
  reasonCodes: {
    list: () => proxyFetch<ReasonCode[]>("/v1/admin/inventory/reason-codes"),
  },
  lowStock: {
    list: (filter: { warehouseId?: string; cursor?: string }) => {
      const qs = new URLSearchParams();
      for (const [k, v] of Object.entries(filter)) {
        if (v !== undefined && v !== null && v !== "") qs.set(k, String(v));
      }
      const suffix = qs.toString() ? `?${qs.toString()}` : "";
      return proxyFetch<{ rows: LowStockRow[]; nextCursor: string | null }>(
        `/v1/admin/inventory/low-stock${suffix}`,
      );
    },
    setThreshold: (skuId: string, warehouseId: string, threshold: number, rowVersion: number) =>
      proxyFetch<LowStockRow>(
        `/v1/admin/inventory/low-stock/${encodeURIComponent(skuId)}/threshold`,
        {
          method: "PUT",
          body: JSON.stringify({ warehouseId, threshold, rowVersion }),
        },
      ),
  },
  batches: {
    list: (filter: { warehouseId?: string; skuId?: string }) => {
      const qs = new URLSearchParams();
      for (const [k, v] of Object.entries(filter)) {
        if (v !== undefined && v !== null && v !== "") qs.set(k, String(v));
      }
      const suffix = qs.toString() ? `?${qs.toString()}` : "";
      return proxyFetch<Batch[]>(`/v1/admin/inventory/batches${suffix}`);
    },
    create: (payload: BatchPayload) =>
      proxyFetch<Batch>("/v1/admin/inventory/batches", {
        method: "POST",
        body: JSON.stringify(payload),
      }),
    update: (id: string, payload: Partial<BatchPayload> & { rowVersion: number }) =>
      proxyFetch<Batch>(`/v1/admin/inventory/batches/${encodeURIComponent(id)}`, {
        method: "PUT",
        body: JSON.stringify(payload),
      }),
  },
  reservations: {
    list: (filter: { warehouseId?: string; skuId?: string; cursor?: string }) => {
      const qs = new URLSearchParams();
      for (const [k, v] of Object.entries(filter)) {
        if (v !== undefined && v !== null && v !== "") qs.set(k, String(v));
      }
      const suffix = qs.toString() ? `?${qs.toString()}` : "";
      return proxyFetch<{ rows: Reservation[]; nextCursor: string | null }>(
        `/v1/admin/inventory/reservations${suffix}`,
      );
    },
    release: (id: string, idempotencyKey: string) =>
      proxyFetch<void>(
        `/v1/admin/inventory/reservations/${encodeURIComponent(id)}/release`,
        { method: "POST", idempotencyKey },
      ),
  },
  ledger: {
    list: (filter: {
      skuId?: string;
      warehouseId?: string;
      from?: string;
      to?: string;
      cursor?: string;
    }) => {
      const qs = new URLSearchParams();
      for (const [k, v] of Object.entries(filter)) {
        if (v !== undefined && v !== null && v !== "") qs.set(k, String(v));
      }
      const suffix = qs.toString() ? `?${qs.toString()}` : "";
      return proxyFetch<LedgerPage>(`/v1/admin/inventory/ledger${suffix}`);
    },
    exportCreate: (filter: {
      skuId?: string;
      warehouseId?: string;
      from?: string;
      to?: string;
    }) =>
      proxyFetch<ExportJob>("/v1/admin/inventory/ledger/export", {
        method: "POST",
        body: JSON.stringify(filter),
      }),
    exportStatus: (jobId: string) =>
      proxyFetch<ExportJob>(
        `/v1/admin/inventory/ledger/export/${encodeURIComponent(jobId)}`,
      ),
  },
};
