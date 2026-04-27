/**
 * T006 — SM-1 (product state).
 *
 * States: Draft ↔ Scheduled ↔ Published ↔ Discarded.
 * Transitions are derived from spec 005's product state machine. The UI
 * uses this module to (a) render the state pill, (b) gate the publish /
 * schedule / discard / revert actions, (c) map server responses back
 * onto the local model.
 */
import type { ProductState } from "@/lib/api/clients/catalog";

export interface ProductStateTransition {
  from: ProductState;
  to: ProductState;
  /** Action label key in messages.catalog.product.publish_controls.* */
  actionKey:
    | "publish_now"
    | "schedule_publish"
    | "revert_to_draft"
    | "discard"
    | "restore_revision"
    | "unschedule";
  /** Permission required to perform the transition (logical AND with route gates). */
  requiredPermissions: string[];
}

/** Extended state used only by the client to model the discarded outcome. */
export type ClientProductState = ProductState | "discarded";

export const PRODUCT_TRANSITIONS: ProductStateTransition[] = [
  {
    from: "draft",
    to: "published",
    actionKey: "publish_now",
    requiredPermissions: ["catalog.product.write"],
  },
  {
    from: "draft",
    to: "scheduled",
    actionKey: "schedule_publish",
    requiredPermissions: ["catalog.product.write"],
  },
  {
    from: "scheduled",
    to: "published",
    actionKey: "publish_now",
    requiredPermissions: ["catalog.product.write"],
  },
  {
    from: "scheduled",
    to: "draft",
    actionKey: "unschedule",
    requiredPermissions: ["catalog.product.write"],
  },
  {
    from: "published",
    to: "draft",
    actionKey: "revert_to_draft",
    requiredPermissions: ["catalog.product.write"],
  },
];

export function allowedTransitions(
  state: ClientProductState,
): ProductStateTransition[] {
  if (state === "discarded") return [];
  return PRODUCT_TRANSITIONS.filter((t) => t.from === state);
}

export function canTransition(
  state: ClientProductState,
  to: ProductState,
): boolean {
  return allowedTransitions(state).some((t) => t.to === to);
}

/**
 * UI sugar — returns the pill label key and tone for a given state.
 * `scheduled` rows additionally render the scheduled timestamp downstream.
 */
export function pillFor(state: ClientProductState): {
  labelKey: string;
  tone: "neutral" | "info" | "success" | "warning";
} {
  switch (state) {
    case "draft":
      return { labelKey: "catalog.product.state.draft", tone: "neutral" };
    case "scheduled":
      return { labelKey: "catalog.product.state.scheduled", tone: "info" };
    case "published":
      return { labelKey: "catalog.product.state.published", tone: "success" };
    case "discarded":
      return { labelKey: "catalog.product.state.discarded", tone: "warning" };
  }
}
