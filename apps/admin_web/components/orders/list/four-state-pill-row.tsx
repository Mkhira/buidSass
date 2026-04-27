/**
 * T024 — four-state pill row (FR-005).
 *
 * Renders the four independent signals as separate pills. NEVER
 * collapses into a single status — Constitution Principle 17.
 */
"use client";

import { StatePill } from "../shared/state-pill";

export interface FourStatePillRowProps {
  orderState: string;
  paymentState: string;
  fulfillmentState: string;
  refundState: string;
}

export function FourStatePillRow({
  orderState,
  paymentState,
  fulfillmentState,
  refundState,
}: FourStatePillRowProps) {
  return (
    <div className="flex flex-wrap gap-1">
      <StatePill machine="order" state={orderState} />
      <StatePill machine="payment" state={paymentState} />
      <StatePill machine="fulfillment" state={fulfillmentState} />
      <StatePill machine="refund" state={refundState} />
    </div>
  );
}
