/**
 * T013 — Orders feature flags (research §R7).
 *
 * Flips when adjacent specs ship. Reading via process.env at module
 * load makes these constant per request — fine for v1.
 */
export interface OrdersFeatureFlags {
  /** Spec 019 (admin-customers) — when on, customer chips deep-link to /customers/<id>. */
  adminCustomersShipped: boolean;
  /** Spec 021 (quotes) — when on, source-quote chip is rendered + clickable. */
  adminQuotesShipped: boolean;
  /** Finance export (CSV) availability. */
  financeExportEnabled: boolean;
}

export function ordersFeatureFlags(): OrdersFeatureFlags {
  return {
    adminCustomersShipped:
      process.env.NEXT_PUBLIC_FLAG_ADMIN_CUSTOMERS_SHIPPED === "1",
    adminQuotesShipped:
      process.env.NEXT_PUBLIC_FLAG_ADMIN_QUOTES_SHIPPED === "1",
    financeExportEnabled:
      process.env.NEXT_PUBLIC_FLAG_FINANCE_EXPORT_ENABLED === "1",
  };
}

/** Per-market step-up threshold (minor units). Configurable via env. */
export function stepUpThresholdMinor(market: "ksa" | "eg"): number {
  const ksaDefault = Number(
    process.env.NEXT_PUBLIC_REFUND_STEP_UP_THRESHOLD_KSA_MINOR ?? "100000",
  );
  const egDefault = Number(
    process.env.NEXT_PUBLIC_REFUND_STEP_UP_THRESHOLD_EG_MINOR ?? "1000000",
  );
  return market === "ksa" ? ksaDefault : egDefault;
}
