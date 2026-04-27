# Feature Specification: Admin Customers

**Feature Branch**: `phase-1C-specs`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "Spec 019 admin-customers (Phase 1C) — Next.js admin web feature mounting inside spec 015's shell. Per docs/implementation-plan.md §Phase 1C item 019: depends on spec 004 contract merged to main + spec 015. Exit: customer list, profile detail, verification history, quote history, support-ticket linkage, address book, B2B company hierarchy, admin actions (suspend / unlock / password-reset trigger — audited)."

## Clarifications

### Session 2026-04-27

- Q: When an admin suspends a customer, what is the cascade effect on their active sessions, in-flight orders, and active inventory reservations? → A: **Revoke all sessions immediately** via spec 004 (the customer's next request fails auth); **leave in-flight orders untouched** (fulfillment proceeds — operations cancels separately if needed); **keep active reservations intact** so they expire on their natural TTL (drift surfaces through spec 010 on the customer's next interaction). Suspending is an account-state action, not a fulfillment-state action.
- Q: How should the customer app communicate a suspended account to the customer trying to sign in? → A: **Generic auth-failure** (do not leak the suspension state to the customer-app sign-in surface). The customer is directed to contact support; the support agent reveals the reason. Spec 004's lockout-state catalog already publishes this behavior; this spec consumes it.
- Q: Should the spec reserve any forward-compatible affordance for an admin "log in as customer" / impersonation feature? → A: **No** — fully out of scope, no placeholder, no feature flag, no menu entry. Customer impersonation carries hard compliance + audit requirements that warrant a dedicated spec when (if) the platform decides to ship it. Leaving any affordance visible would make the future feature discoverable in a way that's hard to remove later.
- Q: When an admin without `customers.pii.read` views a profile, should the spec emit an audit event for the redacted page view? → A: **No** — the field-level mask is a UI concern, not a server mutation. Server-side enforcement (spec 004 returns the redacted fields) is the audit-bearing surface. Auditing every redacted page view would flood the audit log without adding accountability over what spec 004 already records.
- Q: What freshness model should the orders-summary chip on the profile page use? → A: **Stale-while-revalidate with a 60 s stale window**. Fresh fetch on profile mount; subsequent mounts within 60 s consume cached data while a background refetch updates it. Order count + most-recent-order id is low-velocity relative to the cadence of a support session; tighter freshness adds backend load without UX gain.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find a customer and review their profile (Priority: P1)

A customer-support admin opens the customers module, filters the list by market + B2B flag + verification state + free-text query (email / phone / display name partial match), opens the customer's profile detail, and sees identity (display name, email, phone, market, locale), role memberships, address book preview, and an orders summary (count + most recent order chip linking to spec 018 detail).

**Why this priority**: This is the daily customer-support entry point. Without it, every support escalation degrades to a database query. With just this story, support teams can identify and contextualize a customer in seconds — every other story is a deeper drill-in.

**Independent Test**: Seed customers across both markets with mixed B2B + verification states. Open the list — every filter narrows correctly + free-text search resolves on partial match. Open one profile — identity card renders, role chips render, the order count + most-recent-order chip resolve correctly with deep links into spec 018.

**Acceptance Scenarios**:

1. **Given** an admin with `customers.read`, **When** they open the customers list, **Then** every customer in their role scope appears with display name, email-or-phone (per their PII permission), market, B2B flag, verification state, last-active timestamp.
2. **Given** the list is open, **When** they apply any combination of market + B2B-tristate + verification-state filter + free-text search, **Then** the list narrows server-side and the URL reflects the active filter set so the view is shareable.
3. **Given** the list, **When** the result count is zero, **Then** an explicit empty-state surface renders with a single "clear filters" affordance.
4. **Given** a customer profile, **When** the admin opens it, **Then** the identity card surfaces display name, email, phone (only for admins with `customers.pii.read`), market, locale, account-creation timestamp, and last-active timestamp.
5. **Given** a customer profile, **When** the admin scrolls the orders-summary card, **Then** it shows total order count + a chip linking to the customer's most recent order in spec 018; the chip falls back to a placeholder when spec 018 hasn't shipped or the admin lacks `orders.read`.

---

### User Story 2 - Take an admin action on a customer's account (Priority: P2)

A customer-support admin investigating an account-takeover report needs to suspend the customer's account, then trigger a password reset; later — when the customer confirms the reset — the admin unlocks the account. Each action goes through a confirmation dialog, requires a step-up MFA assertion, captures a mandatory reason note, and emits an audit event surfaced in spec 015's reader.

**Why this priority**: Account-recovery + abuse-mitigation is launch-blocking for customer-support. P2 because it's lower frequency than profile lookup but higher blast-radius — an admin action that can lock or unlock a customer's access is the kind of thing audit + step-up MFA + reason-note explicitly exist for (Constitution Principle 25).

**Independent Test**: As an admin with the appropriate permissions + an enrolled MFA factor, suspend a customer with a non-empty reason → confirm the customer can no longer sign in via the customer app (cross-app verification) and the audit-log reader shows the suspend entry. Trigger a password reset → confirm spec 004's reset flow fires (a reset link is delivered to the customer per spec 004 → spec 023 channel). Unlock → confirm the customer can sign in again.

**Acceptance Scenarios**:

1. **Given** an admin with `customers.suspend`, **When** they click **Suspend** on a customer profile, **Then** a confirmation dialog opens requiring a reason note (≥ 10 chars) and a step-up MFA assertion before the action commits.
2. **Given** an admin without `customers.suspend`, **When** they open the profile, **Then** the **Suspend** action is hidden — never rendered then 403-on-click.
3. **Given** a suspended customer, **When** the admin clicks **Unlock** (with `customers.unlock`), **Then** the same step-up + reason-note flow runs and on success the customer's `accountState` returns to active.
4. **Given** an admin with `customers.password_reset.trigger`, **When** they click **Trigger password reset**, **Then** the same confirmation + step-up flow runs, spec 004 issues a reset token, and the reset link is delivered through spec 004's existing channels (email or SMS per spec 004's policy).
5. **Given** any admin action commits, **When** it succeeds, **Then** an audit event is emitted with actor + before / after account state + reason note + step-up assertion id, and is visible in spec 015's audit-log reader within the audit-emission SLA.

---

### User Story 3 - Manage addresses and B2B company hierarchy (Priority: P3)

A customer-support admin opens a customer profile, expands the address book, sees every saved address with a default chip on the active one. For B2B customers, the **Company** card surfaces the parent company + linked branches (or, on a company-account profile, the list of member admins / approvers). Clicking a branch / member chip routes to that entity's profile.

**Why this priority**: B2B operators need this for everyday support ("which branch placed this order?", "who approved this PO?"). P3 because the consumer flow doesn't depend on it; B2B admins who run multi-branch dental groups do.

**Independent Test**: For a B2C customer, confirm the address book renders with default + non-default entries. For a B2B customer, confirm the **Company** card renders the parent + branches; clicking a branch opens its profile, and the originating customer profile is reachable via the back affordance. For an admin without `customers.b2b.read`, confirm the **Company** card is hidden.

**Acceptance Scenarios**:

1. **Given** any customer profile, **When** the admin expands the address book, **Then** every saved address is listed with a default chip on the active one + per-address market badge.
2. **Given** a B2B customer profile, **When** the **Company** card mounts, **Then** the parent company name + branches list (or member-admins list, depending on the entity kind) render; each entry is a clickable chip routing to that entity's profile.
3. **Given** an admin without `customers.b2b.read`, **When** they open a B2B customer profile, **Then** the **Company** card is hidden; only the consumer-side fields render.
4. **Given** a customer with no saved addresses, **When** the admin expands the address book, **Then** an empty-state row renders with a clear "no addresses on file" message.

---

### User Story 4 - Read history across verification, quotes, and support tickets (Priority: P4)

A customer-support admin investigating a complex case opens a profile and scrolls through:

- **Verification history** — every submission + decision (consumes spec 020 when shipped; placeholder before).
- **Quote history** — every quote requested / approved / converted-to-order (consumes spec 021 when shipped; placeholder before).
- **Support tickets** — every ticket opened by or about this customer (consumes spec 023 when shipped; placeholder before).

**Why this priority**: Reduces support-escalation handoffs ("can you check if this customer's verification was rejected?"). Lower priority because every panel here lives behind a feature flag tied to its owning spec; v1 ships the panels with placeholder bodies that flip on the day each upstream spec ships.

**Independent Test**: Confirm each panel renders. Where the corresponding spec is shipped, confirm rows render with deep links + the appropriate permission gate. Where the spec is **not** shipped, confirm the panel surfaces a localized "coming soon" placeholder with a copy-customer-id affordance.

**Acceptance Scenarios**:

1. **Given** a customer profile, **When** the admin scrolls to the **Verification history** panel and `flags.adminVerificationsShipped` is `true`, **Then** every submission + admin decision renders with timestamp + actor + decision badge.
2. **Given** the same panel and `flags.adminVerificationsShipped` is `false`, **When** the panel mounts, **Then** a localized placeholder renders with a copy-id affordance and a one-line explanation.
3. **Given** the same logic applied to the **Quote history** panel (`flags.adminQuotesShipped`) and the **Support tickets** panel (`flags.adminSupportShipped`), the same gating semantics hold.

### Edge Cases

- Two admins suspend the same customer simultaneously → spec 004's row-version optimistic concurrency rejects the second save with a 412; the dialog surfaces a "another admin updated this account; reload?" overlay preserving the typed reason note.
- Step-up assertion expires mid-action → the action submit returns a fresh step-up-required response; the dialog re-prompts and resubmits with the new assertion id.
- An admin trying to suspend their own account → blocked client-side (with a clear localized error) and server-side (spec 004 enforces).
- Admin's permission revoked mid-action → next API call returns 403; the editor surfaces the same screen the shell uses for direct-403 navigation.
- A B2B branch profile that's been deactivated → renders read-only with a localized "this branch is inactive" badge; admin actions are uniformly hidden.
- The most-recent-order chip points at an order whose owning admin spec (018) hasn't shipped → chip falls back to "order detail coming soon" + copy-id affordance.
- Locale switched during an admin action → the action still completes server-side; the success toast renders in the new locale; reason note text preserved verbatim (not retranslated).
- Filter combination returning > 100k rows → server-side pagination keeps the UI responsive; the count chip surfaces "100,000+ customers match" rather than the exact count.
- Filter applied to a free-text search that returns nothing → empty state renders with a single "clear filters" affordance.

## Requirements *(mandatory)*

### Functional Requirements

#### Shell + nav

- **FR-001**: The customers module MUST mount inside spec 015's admin shell — a sidebar entry "Customers" with sub-entries:
  - **Customers** — the unfiltered list at `/customers`. Always visible when `customers.read` is held.
  - **Companies** — a market-level B2B index at `/customers?roleScope=company` (a saved-view-equivalent pre-filter against the same list, filtering to `customer.company_owner` rows). Visible only when `customers.b2b.read` is held. Per-company drill happens via the customer profile's Company card (FR-020); this entry is the "browse all companies" entry point. No separate page is introduced — the entry is a pre-filtered list view.
  - **Suspended** — a pre-filtered list at `/customers?accountState=suspended`. Always visible when `customers.read` is held. Same data shape as the Customers list; the filter chip is preset and cannot be removed without leaving the entry.
- **FR-002**: Every page MUST use spec 015's shell primitives (`AppShell`, `DataTable`, `FormBuilder`, state primitives) — no reimplementation.
- **FR-003**: Every page MUST be keyboard-navigable + WCAG 2.1 AA (inherits spec 015's a11y bar).

#### Customer list

- **FR-004**: The list MUST use spec 015's shared `DataTable` with: server-side cursor pagination, saved views, a free-text search bar (email / phone / display name partial match — server-side only). Multi-select is NOT shipped in v1 (matches spec 018 — no checkbox column).
- **FR-005**: The list MUST support filters: market (single-select), B2B (tristate: true / false / unset), verification state (multi-select), account state (multi-select: `active` / `suspended` / `closed`). The active filter set MUST be reflected in the URL. **Note**: `closed` accounts are filterable for read access (e.g., support investigating a closed account's history), but no admin action in this spec produces or operates on the `closed` state. Account closure originates from the customer-app's account-deletion flow (a future spec) or from spec 004's lockout state machine; this spec just renders whatever state spec 004 returns.
- **FR-006**: The list MUST surface an explicit empty state when zero rows match the active filter set, with a single "clear filters" affordance.
- **FR-007**: PII columns (email, phone) MUST render only for admins with `customers.pii.read`; otherwise the columns are replaced with masked placeholders (`••• @•••.com`, `+••• ••• ••• ••12`). The mask is a **defence-in-depth** — server-side enforcement in spec 004 is the source of truth (the API returns redacted values for admins lacking the permission). Field-level redacted views MUST NOT emit audit events on every page render — auditing happens at the server's data-access layer, not at the UI.
- **FR-007a**: The `<MaskedField>` component (promoted to spec 015's shell per FR-025) MUST emit a single telemetry event `customers.pii.field.rendered` with property `mode: 'masked' | 'unmasked'` exactly once per profile / list-row mount (debounced; not per re-render). The event carries no value, no customer id, no field name beyond `kind: 'email' | 'phone' | 'generic'`. Operations uses the event ratio (`unmasked / masked`) as a regression signal — a sudden jump in the unmasked share for a permission profile that should be masked surfaces an unintended PII leak before users notice. Test `tests/unit/customers/telemetry.pii-mask.test.tsx` asserts a permission-less render emits `mode: 'masked'`.

#### Customer profile detail

- **FR-008**: The profile MUST show: identity card (display name, email, phone, market, locale, account creation, last-active), role chips (e.g., `customer.standard`, `customer.company_owner`), address book preview (top 3 + "view all"), orders summary (count + most-recent-order chip), and — for any customer in `accountState = suspended` — a read-only **Suspension** card showing the most recent suspend reason note + actor + timestamp. The Suspension card consumes spec 004's lockout-state record when published; until that endpoint exists, the card surfaces a "see audit log for reason" link deep-linking into the corresponding spec 015 audit entry (per the `customers.account.suspended` action key).
- **FR-009**: The orders-summary chip MUST deep-link to spec 018's order detail when shipped (`flags.adminOrdersShipped`); otherwise render a placeholder with a copy-id affordance. The order-count + most-recent-order data MUST use a **stale-while-revalidate** cache with a 60 s stale window — fresh fetch on profile mount, cached data on subsequent mounts within 60 s with a background refetch updating it.
- **FR-010**: The address book preview MUST surface a default chip on the active default address; clicking "view all" expands the full list within the same page (not a route push).
- **FR-011**: B2B-only fields (PO defaults, approver settings) MUST render only on B2B-profile types and only for admins with `customers.b2b.read`.

#### Admin actions (suspend / unlock / password-reset trigger)

- **FR-012**: Each admin action MUST be presented as a button under an **Account actions** section on the profile detail. The button is **hidden** (not rendered-and-403-on-click) when the actor lacks the required permission.
- **FR-013**: Each admin action MUST require: (a) a confirmation dialog citing the action and target, (b) a mandatory free-text reason note (≥ 10 chars, ≤ 2000), (c) a fresh step-up MFA assertion via spec 004's step-up flow.
- **FR-014**: Suspend MUST flip the customer's `accountState` to `suspended` server-side. The cascade MUST: (a) revoke all the customer's active sessions via spec 004 — atomically with the `accountState` flip if spec 004 exposes a single endpoint, otherwise as a follow-up worker spec 004 owns; (b) leave in-flight orders untouched (operations cancels separately if needed via 018 FR-012a); (c) leave active inventory reservations intact to expire on their natural TTL; (d) leave the server-side cart intact — its reservations expire with the cart's TTL, and a guest token cannot resume an authenticated cart, so the cart is effectively unreachable. Subsequent customer-app sign-in attempts MUST fail with a **generic auth-failure** — the suspension state MUST NOT be revealed on the customer-app sign-in surface (the customer is directed to contact support, which reveals the reason via the support channel).
- **FR-014a**: When spec 004's suspend endpoint is **non-atomic** (i.e., session revocation runs as a follow-up worker), the admin UI MUST surface a transient "session revoke pending — sessions will end within 60 s" inline status on the profile detail until spec 004 confirms the revoke completed. The UI MUST NOT claim immediate revocation when the contract has not yet completed it. The atomicity choice is owned by spec 004; this spec consumes whichever shape ships and adapts the status copy accordingly. An issue `spec-004:gap:suspend-cascade-atomicity` is filed to prefer the atomic shape.
- **FR-015**: Unlock MUST flip `accountState` back to `active`.
- **FR-016**: Password-reset trigger MUST call spec 004's admin-trigger-reset endpoint; spec 004 issues the reset token + delivers the reset link through its existing channel (email / SMS).
- **FR-017**: An admin MUST NOT suspend their own account (client-side guard + server-side guard).
- **FR-018**: Every admin action emits an audit event server-side with actor + before / after `accountState` + reason note + step-up assertion id, surfaced in spec 015's reader.
- **FR-018a**: The customer profile page header MUST surface an `<AuditForResourceLink resourceType="Customer" resourceId={customerId} />` (spec 015 FR-028f) for admins with `audit.read`.

#### Address book + B2B company hierarchy

- **FR-019**: The address book MUST be **read-only** in v1 — admins do not edit / delete customer addresses through this surface (the customer owns their address book through spec 014).
- **FR-020**: The **Company** card on a B2B customer profile MUST render: parent company name, branches list (with chips routing to each branch's profile), member-admins list (on a company-account profile), approver settings preview (read-only).
- **FR-021**: The **Company** card MUST be hidden when the actor lacks `customers.b2b.read`.
- **FR-021a**: B2B Company **creation** is **out of scope** for this spec. Companies materialize in spec 004 via either (a) self-registration from the customer app's B2B onboarding flow (owned by spec 021 when shipped) or (b) admin-driven creation via spec 021's company-CRUD UI. This spec consumes whichever creation path 021 ships and reads the resulting hierarchy. Until 021 ships, ops creates companies via the spec 004 seeder / admin-CLI; the customers list still surfaces those companies via the existing read endpoint.

#### Cross-spec history panels (placeholders until owning specs ship)

- **FR-022**: The **Verification history** panel MUST render rows from spec 020 when `flags.adminVerificationsShipped` is on; otherwise render a localized placeholder.
- **FR-023**: The **Quote history** panel MUST render rows from spec 021 when `flags.adminQuotesShipped` is on; otherwise render a localized placeholder.
- **FR-024**: The **Support tickets** panel MUST render rows from spec 023 when `flags.adminSupportShipped` is on; otherwise render a localized placeholder.
- **FR-025**: Each panel's placeholder MUST display a copy-customer-id affordance + a single sentence explaining what'll appear when the owning spec ships.

#### Architectural guardrails

- **FR-025a**: The full set of permission keys this spec consumes — `customers.read`, `customers.pii.read`, `customers.b2b.read`, `customers.suspend`, `customers.unlock`, `customers.password_reset.trigger` — MUST be registered in spec 015's `contracts/permission-catalog.md` (see spec 015 FR-028b). `customers.pii.read` is intentionally distinct from spec 018's `orders.pii.read`; see spec 018 FR-023a.
- **FR-026**: This spec MUST NOT modify any backend contract. Gaps escalate to spec 004 (or to specs 020 / 021 / 023 when their panels are wired) per Phase 1C intent.
- **FR-027**: All API access MUST go through spec 015's auth proxy + generated typed clients; no ad-hoc fetch.
- **FR-028**: Both Arabic and English MUST be fully supported with full RTL when AR is active; verification-state labels, account-state labels, role labels MUST be localized via the i18n layer.

### Key Entities *(client-side state — no backend persistence introduced)*

- **CustomerListRow**: id, display name, email-or-masked, phone-or-masked, market, B2B flag, verification state, account state, last-active timestamp, created-at.
- **CustomerProfile**: identity fields (above), role chips, addresses (top 3 preview + count of remaining), order summary (count + most-recent-order chip), B2B Company linkage (when present), row version.
- **AccountActionDraft**: action kind (`suspend` / `unlock` / `password_reset_trigger`), reason note, idempotency key, step-up assertion id (populated after step-up dialog).
- **CompanyHierarchyView**: parent company id + name, branches list (id + name + active flag), member-admins list (id + display + role), approver settings preview.
- **HistoryPanelEntry**: per-panel shape — verification history rows, quote history rows, support-ticket rows; each row carries a deep-link target + a stable id for the placeholder fallback.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A customer-support admin can locate a customer (by email / phone / display name) and open their profile in under 30 seconds from login on the typical staging dataset.
- **SC-002**: The customers list's first page returns in under 1 second on the staging dataset (target: 1M lifetime customers, 50k active).
- **SC-003**: 100 % of customers screens render correctly in both Arabic-RTL and English-LTR (inherits spec 015's visual-regression mechanism).
- **SC-004**: 0 admin actions render as enabled then 403-on-click — measured by an automated check that walks every action × every permission profile (FR-012, mirrors spec 018's SC-004 contract test pattern).
- **SC-005**: 0 admin actions persist without a corresponding audit event with the step-up assertion id (verified by a periodic reconciliation script — owned by ops).
- **SC-006**: Profile detail page first interactive ≤ 1.5 seconds on broadband.
- **SC-007**: 0 customer email / phone values render to admins lacking `customers.pii.read` — verified by a unit test sweeping every component that consumes the customer view-model.
- **SC-008**: 0 user-visible English strings on any customers screen when the active locale is Arabic.
- **SC-009**: 0 backend contract changes shipped from this spec.

## Assumptions

- **Spec 015 shell** — shipped. Inherits auth proxy, `DataTable`, `FormBuilder`, audit-log reader, AR/RTL plumbing, step-up dialog primitive (introduced in spec 018 — reused here).
- **Spec 004 contracts merged** — required by the implementation plan. Provides customer list / profile / role chips / suspend / unlock / password-reset-trigger / B2B-company endpoints.
- **PII-permission model** — `customers.pii.read` controls visibility of email + phone in lists and profile detail. Admins without it see masked placeholders. Server-side, spec 004 also enforces; the client mask is a defence-in-depth.
- **Step-up reuse** — the step-up dialog primitive shipped by spec 018 (`<StepUpDialog>`) is **promoted to a shared component** in spec 015's shell and reused unchanged here. Spec 015 may need an editor-side carry-back PR; the gap is escalated against spec 015 if not already shared.
- **Address-book editing** — read-only in v1 (FR-019). Customer owns the source of truth via spec 014. A future support-driven address-edit flow lands as a separate spec when operations need it.
- **Cross-spec panels behind feature flags** — `flags.adminVerificationsShipped`, `flags.adminQuotesShipped`, `flags.adminSupportShipped`. Off by default; flipped in deployments when the owning spec ships.
- **Spec 018 deep-link** — the orders-summary chip uses `flags.adminOrdersShipped` mirroring how 018 uses the customer-chip flag. Both flags flip together in a deployment when both specs ship.
- **B2B model** — assumes the spec 004 + spec 021 model where a B2B account is one of three kinds: `customer.standard`, `customer.company_owner`, `customer.company_member`. The Company card surfaces only for the latter two.
- **Saved views** — list saved views live on spec 004's user-preferences endpoint (same channel spec 015 / 016 / 017 / 018 use).

## Dependencies

- **Spec 003 (foundations)** — audit emission.
- **Spec 004 (identity & access)** — every customer CRUD-read / suspend / unlock / password-reset-trigger / role / B2B-company / address-list endpoint.
- **Spec 015 (admin foundation)** — shell, auth proxy, DataTable, FormBuilder, audit-log reader.
- **Spec 018 (admin orders)** — orders-summary chip target (degrades gracefully when not shipped).
- **Spec 020 (verification)** — verification-history panel feed (placeholder until shipped).
- **Spec 021 (B2B / quotes)** — quote-history panel feed + B2B-company depth (placeholder until shipped).
- **Spec 023 (notifications + support)** — support-ticket panel feed (placeholder until shipped).

## Out of Scope (this spec)

- Customer creation by admin (registration is owned by the customer app + spec 004).
- Customer profile editing by admin (read-only profile in v1; edits flow through customer app).
- Address-book editing by admin (FR-019).
- Verification approval workflow (spec 020).
- Quote approval / rejection workflow (spec 021).
- Support-ticket reply UI (spec 023).
- Bulk admin actions (deferred — no checkbox column in v1).
- Customer impersonation / "log in as customer" feature — explicitly out of scope. **No placeholder, no feature flag, no menu entry** ships in this spec. If the platform decides to ship impersonation later, it requires a dedicated spec covering audit, scope-of-action restrictions, time-boxing, and customer-notification policy.
