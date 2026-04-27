/**
 * T074a (FR-022a): client-side mirror of `contracts/audit-redaction.md`.
 *
 * The server pre-redacts JSON paths the calling admin's permission set
 * doesn't grant. This file is the **defence-in-depth** path: even if
 * the server-side response slipped a sensitive value through (stale
 * cache, partial-permission upgrade), the client still wraps it in
 * `<MaskedField>` based on the rules below.
 *
 * Drift CI (`pnpm catalog:check-audit-redaction`, T032g) walks audit
 * fixtures and asserts every sensitive path here OR in the markdown
 * registry has matching server-side enforcement.
 */

export type Permission =
  | "audit.read"
  | "customers.read"
  | "customers.pii.read"
  | "orders.read"
  | "orders.pii.read"
  | "orders.refund.initiate"
  | "orders.cancel"
  | "customers.suspend"
  | "customers.unlock"
  | "customers.password_reset.trigger"
  | "catalog.product.read";

export interface RedactionRule {
  /** JSON path with `*` for any array index or any object key. */
  path: string;
  /** The actor needs at least one of these permissions to see the field unredacted. */
  permissions: Permission[];
  /** Field kind for `<MaskedField>` glyph selection. */
  kind: "email" | "phone" | "generic";
}

/**
 * Initial registry — mirrors the shipped sections of
 * `contracts/audit-redaction.md`. New emitters append here in the same
 * PR that registers the path in the markdown.
 */
export const REDACTION_RULES: RedactionRule[] = [
  // Spec 004 (identity) — customer-related events
  { path: "customer.email", permissions: ["customers.pii.read", "orders.pii.read"], kind: "email" },
  { path: "customer.phone", permissions: ["customers.pii.read", "orders.pii.read"], kind: "phone" },
  { path: "address.line1", permissions: ["customers.pii.read"], kind: "generic" },
  { path: "address.line2", permissions: ["customers.pii.read"], kind: "generic" },
  { path: "address.phone", permissions: ["customers.pii.read"], kind: "phone" },
  { path: "address.postalCode", permissions: ["customers.pii.read"], kind: "generic" },
  { path: "address.recipient", permissions: ["customers.pii.read"], kind: "generic" },
  { path: "lockoutState.reasonNote", permissions: ["customers.read"], kind: "generic" },

  // Spec 016 (catalog)
  { path: "restrictedRationale.ar", permissions: ["catalog.product.read"], kind: "generic" },
  { path: "restrictedRationale.en", permissions: ["catalog.product.read"], kind: "generic" },

  // Spec 018 (orders)
  { path: "refund.reasonNote", permissions: ["orders.refund.initiate", "orders.read"], kind: "generic" },
  { path: "cancel.reasonNote", permissions: ["orders.cancel", "orders.read"], kind: "generic" },

  // Spec 019 (customers)
  {
    path: "accountAction.reasonNote",
    permissions: ["customers.suspend", "customers.unlock", "customers.password_reset.trigger"],
    kind: "generic",
  },
];

function matchPath(actual: string, rulePath: string): boolean {
  const segments = rulePath.split(".");
  const actualSegments = actual.split(".");
  if (segments.length !== actualSegments.length) return false;
  return segments.every((seg, i) => seg === "*" || seg === actualSegments[i]);
}

export interface RedactionDecision {
  redact: boolean;
  kind?: "email" | "phone" | "generic";
}

/**
 * Returns whether the given JSON path should be redacted for an actor
 * holding the given permission set.
 */
export function decideRedaction(jsonPath: string, actorPermissions: ReadonlySet<string>): RedactionDecision {
  // Strip leading `before.` / `after.` / `metadata.` so rules match
  // either side of the audit blob.
  const normalized = jsonPath.replace(/^(?:before|after|metadata)\./, "");
  for (const rule of REDACTION_RULES) {
    if (!matchPath(normalized, rule.path)) continue;
    const allowed = rule.permissions.some((p) => actorPermissions.has(p));
    if (!allowed) return { redact: true, kind: rule.kind };
    return { redact: false };
  }
  return { redact: false };
}
