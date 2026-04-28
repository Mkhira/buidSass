/**
 * T008 — Transition gate.
 *
 * Pure decision function consumed by `<TransitionActionBar>` and
 * `<TransitionActionButton>`. Mirrors the server's authority for
 * SC-004 (no 403 after render): the gate's `'render'` decision is
 * what spec 011 will accept; `'hide'` / `'render_disabled'` decisions
 * the server would refuse.
 */

export type Machine = "order" | "payment" | "fulfillment" | "refund";

export type TransitionDecision =
  | {
      kind: "render";
      actionKey: string;
      requiredPermission: string;
      labelKey: string;
      toState: string;
    }
  | {
      kind: "hide";
      reason: "permission_missing" | "state_machine_disallowed";
    }
  | {
      kind: "render_disabled";
      reason: "order_closed" | "shipment_blocking";
      labelKey: string;
    };

export interface TransitionRule {
  machine: Machine;
  fromState: string;
  toState: string;
  actionKey: string;
  labelKey: string;
  requiredPermission: string;
  /** Predicate to render-disabled (clickable, but explanation surfaced). */
  renderDisabledIf?: (input: TransitionInput) => "order_closed" | "shipment_blocking" | null;
}

export interface TransitionInput {
  machine: Machine;
  fromState: string;
  toState: string;
  permissions: ReadonlySet<string>;
  /** Whether the order's terminal state has been reached (cancelled/refunded). */
  orderClosed?: boolean;
  /** Whether at least one shipment is blocking a refund-state advance. */
  shipmentBlocking?: boolean;
}

/**
 * Order transitions (subset for v1 — common fulfillment flow).
 *
 * Real catalog comes from spec 011's state-machine doc; this is the
 * v1 minimum viable list. Adding a row here is one PR.
 */
export const TRANSITIONS: TransitionRule[] = [
  // Order machine
  {
    machine: "order",
    fromState: "placed",
    toState: "confirmed",
    actionKey: "confirm_order",
    labelKey: "orders.transitions.confirm_order",
    requiredPermission: "orders.transition.order",
  },
  {
    machine: "order",
    fromState: "confirmed",
    toState: "cancelled",
    actionKey: "cancel_order",
    labelKey: "orders.transitions.cancel_order",
    requiredPermission: "orders.transition.order",
  },

  // Fulfillment machine
  {
    machine: "fulfillment",
    fromState: "pending",
    toState: "packed",
    actionKey: "mark_packed",
    labelKey: "orders.transitions.mark_packed",
    requiredPermission: "orders.transition.fulfillment",
  },
  {
    machine: "fulfillment",
    fromState: "packed",
    toState: "handed_to_carrier",
    actionKey: "hand_to_carrier",
    labelKey: "orders.transitions.hand_to_carrier",
    requiredPermission: "orders.transition.fulfillment",
  },
  {
    machine: "fulfillment",
    fromState: "handed_to_carrier",
    toState: "delivered",
    actionKey: "mark_delivered",
    labelKey: "orders.transitions.mark_delivered",
    requiredPermission: "orders.transition.fulfillment",
  },

  // Payment machine
  {
    machine: "payment",
    fromState: "authorized",
    toState: "captured",
    actionKey: "capture_payment",
    labelKey: "orders.transitions.capture_payment",
    requiredPermission: "orders.transition.payment",
  },
];

export function evaluateTransition(input: TransitionInput): TransitionDecision {
  const rule = TRANSITIONS.find(
    (r) =>
      r.machine === input.machine &&
      r.fromState === input.fromState &&
      r.toState === input.toState,
  );
  if (!rule) {
    return { kind: "hide", reason: "state_machine_disallowed" };
  }
  if (!input.permissions.has(rule.requiredPermission)) {
    return { kind: "hide", reason: "permission_missing" };
  }
  if (rule.renderDisabledIf) {
    const reason = rule.renderDisabledIf(input);
    if (reason !== null && reason !== undefined) {
      return { kind: "render_disabled", reason, labelKey: rule.labelKey };
    }
  }
  if (input.orderClosed) {
    return {
      kind: "render_disabled",
      reason: "order_closed",
      labelKey: rule.labelKey,
    };
  }
  return {
    kind: "render",
    actionKey: rule.actionKey,
    requiredPermission: rule.requiredPermission,
    labelKey: rule.labelKey,
    toState: rule.toState,
  };
}

/**
 * Returns every candidate transition from a given state, evaluated.
 * The action bar consumes this list and renders only `'render'` rows
 * (FR-010); `'render_disabled'` rows surface as disabled buttons with
 * tooltip explanations.
 */
export function candidateTransitions(input: {
  machine: Machine;
  fromState: string;
  permissions: ReadonlySet<string>;
  orderClosed?: boolean;
  shipmentBlocking?: boolean;
}): TransitionDecision[] {
  const candidates = TRANSITIONS.filter(
    (r) => r.machine === input.machine && r.fromState === input.fromState,
  );
  return candidates.map((c) =>
    evaluateTransition({
      machine: c.machine,
      fromState: c.fromState,
      toState: c.toState,
      permissions: input.permissions,
      orderClosed: input.orderClosed,
      shipmentBlocking: input.shipmentBlocking,
    }),
  );
}
