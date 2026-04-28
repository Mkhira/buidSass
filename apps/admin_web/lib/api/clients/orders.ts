/**
 * T005 — spec 011 orders client.
 *
 * Hand-typed mirrors of `data-model.md` until `pnpm gen:api` against
 * `services/backend_api/openapi.orders.json` lands generated types
 * under `lib/api/types/orders.ts`.
 */
import { proxyFetch } from "@/lib/api/proxy";

export interface OrderListRow {
  id: string;
  number: string;
  customer: { id: string; displayName: string; b2b: boolean };
  marketCode: "ksa" | "eg";
  b2bFlag: boolean;
  orderState: string;
  paymentState: string;
  fulfillmentState: string;
  refundState: string;
  grandTotalMinor: number;
  currency: string;
  placedAt: string;
}

export interface OrderListPage {
  rows: OrderListRow[];
  nextCursor: string | null;
}

export interface OrdersListFilters {
  orderStates?: string[];
  paymentStates?: string[];
  fulfillmentStates?: string[];
  refundStates?: string[];
  marketCode?: "ksa" | "eg" | null;
  b2bFlag?: boolean | null;
  placedAtFrom?: string | null;
  placedAtTo?: string | null;
  searchQuery?: string | null;
  cursor?: string;
}

export interface LineItem {
  id: string;
  productId: string;
  sku: string;
  name: { en: string; ar: string };
  qty: number;
  deliveredQty: number;
  alreadyRefundedQty: number;
  unitPriceMinor: number;
  lineSubtotalMinor: number;
}

export interface Shipment {
  id: string;
  carrierName: string;
  carrierReference: string | null;
  trackingUrl: string | null;
  state: string;
}

export interface TotalsBreakdown {
  subtotalMinor: number;
  discountsMinor: number;
  taxMinor: number;
  shippingMinor: number;
  grandTotalMinor: number;
  currency: string;
}

export interface OrderDetail {
  id: string;
  number: string;
  marketCode: "ksa" | "eg";
  b2bFlag: boolean;
  customer: { id: string; displayName: string; email?: string; phone?: string };
  shippingAddress: {
    line1: string;
    line2?: string | null;
    city: string;
    region?: string | null;
    country: string;
    postalCode: string;
    phone?: string;
  };
  paymentSummary: {
    method: string;
    state: string;
    capturedMinor: number;
    refundedMinor: number;
    currency: string;
  };
  lineItems: LineItem[];
  shipments: Shipment[];
  totals: TotalsBreakdown;
  timelineCursor: string | null;
  rowVersion: number;
  sourceQuoteId: string | null;
  orderState: string;
  paymentState: string;
  fulfillmentState: string;
  refundState: string;
  placedAt: string;
}

export interface TimelineEntry {
  id: string;
  machine: "order" | "payment" | "fulfillment" | "refund";
  fromState: string;
  toState: string;
  actor: {
    kind: "admin" | "customer" | "system";
    id?: string;
    displayName?: string;
  };
  reasonNote: string | null;
  occurredAt: string;
  auditPermalink: string;
  metadata?: Record<string, unknown>;
}

export interface TimelinePage {
  rows: TimelineEntry[];
  nextCursor: string | null;
}

export interface TransitionPayload {
  toState: string;
  reasonNote?: string | null;
  rowVersion: number;
}

export interface OrdersExportJob {
  id: string;
  filterSnapshot: OrdersListFilters;
  status: "queued" | "in_progress" | "done" | "failed";
  progress?: number;
  rowCount: number | null;
  downloadUrl: string | null;
  error: { reasonCode: string; message: string } | null;
  createdAt: string;
}

function toQs(filter: Record<string, unknown>): string {
  const qs = new URLSearchParams();
  for (const [k, v] of Object.entries(filter)) {
    if (v === undefined || v === null || v === "") continue;
    if (Array.isArray(v)) {
      for (const item of v) qs.append(k, String(item));
    } else {
      qs.set(k, String(v));
    }
  }
  return qs.toString() ? `?${qs.toString()}` : "";
}

export const ordersApi = {
  list: (filter: OrdersListFilters) =>
    proxyFetch<OrderListPage>(
      `/v1/admin/orders${toQs(filter as unknown as Record<string, unknown>)}`,
    ),
  detail: (orderId: string) =>
    proxyFetch<OrderDetail>(
      `/v1/admin/orders/${encodeURIComponent(orderId)}`,
    ),
  timeline: (orderId: string, cursor?: string) =>
    proxyFetch<TimelinePage>(
      `/v1/admin/orders/${encodeURIComponent(orderId)}/timeline${toQs({ cursor })}`,
    ),
  transition: (
    orderId: string,
    machine: "order" | "payment" | "fulfillment" | "refund",
    payload: TransitionPayload,
    idempotencyKey: string,
  ) =>
    proxyFetch<OrderDetail>(
      `/v1/admin/orders/${encodeURIComponent(orderId)}/transitions/${machine}`,
      {
        method: "POST",
        body: JSON.stringify(payload),
        idempotencyKey,
      },
    ),
  exports: {
    create: (filter: OrdersListFilters) =>
      proxyFetch<OrdersExportJob>("/v1/admin/orders/exports", {
        method: "POST",
        body: JSON.stringify({ filter }),
      }),
    status: (jobId: string) =>
      proxyFetch<OrdersExportJob>(
        `/v1/admin/orders/exports/${encodeURIComponent(jobId)}`,
      ),
  },
};
