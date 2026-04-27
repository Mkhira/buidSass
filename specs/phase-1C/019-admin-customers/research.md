# Phase 0 Research: Admin Customers

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

Resolves every Technical-Context decision in `plan.md`. Inherits unchanged decisions from specs 015 / 016 / 017 / 018 (Next.js App Router, iron-session auth proxy, openapi-typescript, vitest + Playwright + axe + Storybook, react-query + react-table). Only deltas documented.

---

## R1. PII redaction — `<MaskedField>` single-source component

- **Decision**: A single `<MaskedField kind="email" | "phone" value={raw} canRead={hasPermission} />` component renders either the raw value or a stable masked placeholder (`••• @•••.com`, `+••• ••• ••• ••12`). The component is the only path through which PII is rendered in the customers feature. A vitest sweep `tests/unit/customers/pii-leak.test.tsx` walks every component that consumes `CustomerListRow` / `CustomerProfile` and asserts MaskedField appears in the render tree for permission-less renders.
- The mask is purely a UI defence-in-depth — the spec 004 server already redacts the values for admins lacking `customers.pii.read` (so the field's `value` prop is itself redacted on the wire). The mask handles the edge case where a partial permission upgrade serves raw data + the page still has stale state.
- **a11y**: Screen readers announce the localized "email hidden" / "phone hidden" string, never the mask glyph itself. Tooltip on hover surfaces the same localized string.
- **Alternatives rejected**: per-component ternary checks (drift), CSS-only blur (defeated by selecting + copying), backend-only redaction (loses defence-in-depth on stale state).

## R2. Admin-action gating — pure function shared with spec 018

- **Decision**: Reuse spec 018's `transition-gate` model — a pure function `evaluateAccountAction({ action, customerState, permissions, isSelf }): ActionDecision` returns `{ kind: 'render' | 'hide' | 'render_disabled' }`. The function lives at `apps/admin_web/lib/customers/action-gate.ts` and is exercised by `tests/contract/customers/no-403-after-render.spec.ts`.
- The gate evaluates: (a) does the actor hold the required permission? (b) is the customer's current state a valid pre-state for this action (e.g., suspend requires `accountState !== 'suspended'`)? (c) is the actor attempting to act on themselves? (d) is the customer in a terminal state (e.g., `closed`)?
- **Alternatives rejected**: per-component inline checks (drift), backend-only gate (UI must hide before showing — server-only doesn't help affordance).

## R3. Step-up dialog — promote from spec 018 to shell

- **Decision**: The step-up dialog primitive introduced by spec 018 is **promoted into spec 015's shared shell** at `apps/admin_web/components/shell/step-up-dialog.tsx`. Both specs 018 (refunds) and 019 (account actions) consume it. The dialog calls `/api/auth/step-up/start` + `/api/auth/step-up/complete` (route handlers introduced by 015 / 018) and returns the assertion id; both specs forward it as `X-StepUp-Assertion: <id>`.
- **Rationale**: One step-up flow, one a11y story, one set of integration tests; future admin specs that need step-up plug in without re-implementing.
- **If 018 has not yet promoted the dialog**: file `spec-015:gap:step-up-dialog-promotion` and proceed with a local copy in 019; back-port when 018 ships.
- **Alternatives rejected**: per-feature dialogs (drift), redirect to a step-up page (loses form context).

## R4. Feature-flagged history panels

- **Decision**: A `<FeatureFlaggedPanel>` wrapper component takes a flag-key + an upstream-data hook + a placeholder render and routes accordingly:
  ```tsx
  <FeatureFlaggedPanel
    flag={flags.adminVerificationsShipped}
    placeholder={<VerificationHistoryPlaceholder customerId={...} />}
  >
    <VerificationHistoryList customerId={...} />
  </FeatureFlaggedPanel>
  ```
- Three flags: `adminVerificationsShipped` (spec 020), `adminQuotesShipped` (spec 021), `adminSupportShipped` (spec 023). Flags read from env at build time (`process.env.NEXT_PUBLIC_FLAG_*`); flipping is a deployment-config change, not a code change.
- The `<...Placeholder>` components share a `<HistoryPanelPlaceholder>` shell that renders a localized "coming soon" message + a copy-customer-id affordance. Each panel kind only adds its title and the upstream-spec reference.
- **Alternatives rejected**: hard-coded "coming soon" inline (every flip = code change), graphql-style optional-resolver (overkill for three flags).

## R5. Free-text search — server-side only

- **Decision**: The list's free-text search bar issues a server-side query against spec 004's customer search endpoint with the trimmed query string, debounced 300 ms client-side. Match semantics (which fields match, fuzziness, partial-prefix vs. substring) live entirely on the server. The client only sends the query and renders the result set.
- **Rationale**: Spec 004 already owns the search index for customer-side reasons (forgot-username, etc.). Reusing the same endpoint avoids drift and PII surface duplication.
- **PII consideration**: The search query itself is PII-sensitive (admins typing customer email fragments). The query is **not** logged client-side; the telemetry event records `search_initiated` with no payload value (per `contracts/client-events.md`).
- **Alternatives rejected**: client-side fuzzy search (PII would have to ship to client), separate admin-search index (duplicates spec 004's surface).

## R6. Suspend cascade — explicit client-side awareness

- **Decision**: On a successful suspend, the client invalidates: (a) the customer profile cache (forces re-fetch on next mount, showing the updated `accountState`); (b) the orders-summary cache for that customer (in case fulfillment teams need to see the cascade); (c) the global customers list cache (so suspended customers appear under the "Suspended" sub-entry). It does **not** invalidate the customer-app surface — that's the customer's session being revoked server-side; the customer app handles its own auth-failure on next request.
- **Rationale**: Q1 — server-side cascade revokes sessions but leaves orders / reservations alone. The client mirrors this with surgical cache invalidation rather than a global flush.
- **Alternatives rejected**: global cache flush (over-broad; admins watching unrelated profiles get unnecessary refetches), no invalidation (admin sees stale data after their own action).

## R7. Most-recent-order chip — stale-while-revalidate

- **Decision**: react-query default config for the orders-summary query: `staleTime: 60_000`, `gcTime: 300_000`. Profile mount triggers a fetch; subsequent mounts within 60 s use the cache while a background refetch runs.
- **Rationale**: Q5 chose 60 s stale window. react-query's SWR semantics map directly.
- **Alternatives rejected**: `staleTime: 0` (every mount refetches; tighter freshness, more backend load), `staleTime: ∞` (admin sees stale data unless they explicitly refresh).

## R8. B2B company-hierarchy view

- **Decision**: A flat list (parent → branches → member-admins) rendered in `<CompanyCard>`. For B2B groups with > 50 branches the list virtualizes via `@tanstack/react-virtual` (already in the admin app from 017). Smaller groups render flat.
- The card is hidden for admins without `customers.b2b.read` (FR-021).
- Each chip routes to the corresponding entity's profile (a branch is a customer profile of kind `customer.company_owner`; a member is a customer profile of kind `customer.company_member`).
- **Alternatives rejected**: org-chart visualization (overkill for v1; deferred to a later admin spec when orgs get deep), separate routes per drill (over-fragmented).

## R9. Address book — read-only with default chip

- **Decision**: A simple list rendered by `<AddressBookPreview>` (top 3 + "view all" expanding the list inline) and `<AddressBookExpanded>` (full list on the dedicated page route). Read-only — no edit / delete affordances in v1 (FR-019).
- **Alternatives rejected**: editable address book (out of scope per FR-019 — adds permission + audit + customer-notification surface that warrants a dedicated spec).

## R10. Telemetry events

- **Decision**: Same pattern as 015 / 016 / 017 / 018. New events listed in `contracts/client-events.md`. PII guard rails identical (no customer id, no email value, no phone value, no search query, no reason-note text).
- **PII view audit**: Per Q4, no audit event is emitted on a redacted page view. Telemetry event `customers.profile.opened` is emitted but carries no PII.

## R11. CI integration

- **Decision**: No new workflow file. Inherits `apps/admin_web-ci.yml` from spec 015. The PII-leak unit test (R1) and no-403-after-render contract test (R2) are part of `pnpm test`. Visual regression continues across all admin features. `impeccable-scan` continues advisory.

---

## Open follow-ups for downstream specs

- **Spec 015 / 018 carry-back**: confirm `<StepUpDialog>` is in `apps/admin_web/components/shell/`. If still in `apps/admin_web/components/orders/refund/`, file `spec-015:gap:step-up-dialog-promotion` and back-port from spec 019's branch — both specs benefit.
- **Spec 004**: confirm the customer-search endpoint, suspend / unlock / password-reset-trigger endpoints (with idempotency-key + step-up-assertion-header support), B2B-company endpoints, addresses-list endpoint. Confirm `customers.pii.read` permission key + the redaction semantics on the wire.
- **Spec 020**: when shipped, flip `NEXT_PUBLIC_FLAG_ADMIN_VERIFICATIONS=1`. No code change here.
- **Spec 021**: when shipped, flip `NEXT_PUBLIC_FLAG_ADMIN_QUOTES=1`. The B2B Company card may want deeper integration (approver workflow); that's a 021 surface, not 019.
- **Spec 023**: when shipped, flip `NEXT_PUBLIC_FLAG_ADMIN_SUPPORT=1`. The support-tickets panel feed lives there.
