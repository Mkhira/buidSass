/**
 * SM-2 — History panel mode.
 *
 * Two-state machine driven by env flags at module init. Flips require
 * redeploy; no runtime transitions.
 */

export type HistoryPanelKind = "placeholder" | "shipped";

export interface HistoryPanelFlags {
  adminVerificationsShipped: boolean;
  adminQuotesShipped: boolean;
  adminSupportShipped: boolean;
}

export function customersHistoryPanelFlags(): HistoryPanelFlags {
  return {
    adminVerificationsShipped:
      process.env.NEXT_PUBLIC_FLAG_ADMIN_VERIFICATIONS_SHIPPED === "1",
    adminQuotesShipped:
      process.env.NEXT_PUBLIC_FLAG_ADMIN_QUOTES_SHIPPED === "1",
    adminSupportShipped:
      process.env.NEXT_PUBLIC_FLAG_ADMIN_SUPPORT_SHIPPED === "1",
  };
}

export function modeFor(flag: boolean): HistoryPanelKind {
  return flag ? "shipped" : "placeholder";
}
