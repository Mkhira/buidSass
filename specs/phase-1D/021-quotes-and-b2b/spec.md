# Feature Specification: Quotes and B2B

**Feature Branch**: `phase_1D_creating_specs` (working) · target merge branch: `021-quotes-and-b2b`
**Created**: 2026-04-28
**Status**: Draft
**Constitution**: v1.0.0
**Phase**: 1D (Business Modules · Milestone 7)
**Depends on**: 004 `identity-and-access`, 005 `catalog`, 007-a `pricing-and-tax-engine`, 010 `checkout`, 011 `orders`, 012 `tax-invoices`, 015 `admin-foundation` (contract); 018 `admin-orders` (contract).
**Consumed by**: 007-b `promotions-ux-and-campaigns` (business-pricing scope per company), 011 (quote-to-order conversion), 012 (invoice-billing flag → invoice issuance on terms), 023 `support-tickets` (ticket linkage to a quote), 1.5-c `b2b-reorder-templates` (UI completes the backend template stubs shipped here).
**Input**: User description: "Phase 1D, spec 021 — quotes-and-b2b. Quote request → admin quote → revisions → accept → quote-to-order; company accounts (multi-user with buyer + approver roles + branch hierarchy); PO numbers; invoice-billing flag; explicit quote state machine (requested → drafted → revised → accepted/rejected/expired); customer quote-request flow from cart or from product; admin quote authoring (line-item pricing, terms, validity); approval flow inside company accounts (buyer submits, approver accepts); repeat-order template linkage (backend stubs here, full UI in Phase 1.5-c); synthetic seeder; bilingual AR + EN end-to-end; multi-vendor-ready."

## Clarifications

### Session 2026-04-28

- Q: Multi-approver routing strategy — when a company has multiple approvers, how is a `pending-approver` quote routed and finalized? → A: **Any-approver-finalizes**. All approvers in the company are notified simultaneously when the quote enters `pending-approver`. Any one of them may finalize (→ `accepted`) or reject (→ `revised` with comment); the first action wins, guarded by optimistic concurrency (xmin / version token). Quotes are not bound to a specific approver, so an approver leaving the company does not require re-routing — the remaining approvers can still finalize. If the company has zero approvers at the moment of submission and `approver_required=true`, acceptance is rejected with `quote.no_approver_available` and the buyer is shown a localized message advising the company admin to designate one.
- Q: Company-account verification default state per market at V1 launch — should freshly self-registered companies wait in `pending-verification`? → A: **Default OFF for both KSA and EG at V1 launch**. Self-registered companies are `active` immediately and may transact. Operations may flip the per-market `company_verification_required` toggle to ON post-launch once the moderation queue is staffed; the toggle is a `verification_market_schemas`-style row, no code deploy required. Restricted-SKU eligibility remains separately gated by spec 020's per-buyer professional verification — the company-account verification is a distinct, weaker check (does this company exist? does the tax ID look plausible?), not a substitute for buyer-level verification.
- Q: When `Company.unique_po_required=true`, across what scope must the PO number be unique? → A: **Unique across all quotes ever for that company** (regardless of quote state — terminal or non-terminal). Enforced by a unique index `(company_id, po_number) WHERE po_number IS NOT NULL`. Orders converted from quotes inherit the PO from the quote (back-linked), so the constraint propagates to those orders via the link rather than via separate order-side validation; non-quote orders are out of scope for PO uniqueness. When `unique_po_required=false`, reused PO numbers trigger a soft warning at buyer acceptance prompting confirmation, but the system commits.
- Q: Is a downloadable quote PDF (rendered from each `QuoteVersion`) part of V1, or deferred? → A: **In scope for V1**, reusing spec 012 (`tax-invoices`) PDF generator + storage. Every `QuoteVersion` publish MUST generate a bilingual quote PDF (one EN + one AR per version), persist them as `QuoteVersionDocument` rows linked to the version (storage-key + locale + content-type=`application/pdf`), and surface them to buyer / approver / admin commercial operators via signed-URL download. Customers see download links on the quote view; admin operators may regenerate on demand. Quote PDFs follow the same retention rules as the parent `QuoteVersion` (preserved indefinitely for accepted/expired quotes; purged together with audit-log retention policy if the quote is voided per FR-046).
- Q: When the admin operator extends validity on a revision, how is the new `expires_at` computed? → A: **Reset to `(revision_published_at + market.validity_days)`** (default 14 days from this revision's publish moment). Each extension restarts the validity window from the publish, not from the prior `expires_at`. The operator's choice on each revision is binary — extend (`validity-extends=true` on the new `QuoteVersion` row → recompute `expires_at`) or do not extend (`validity-extends=false` → keep the existing `expires_at`). No date-picker UI; the rule is deterministic and transparent to the buyer. The audit log captures whether each revision extended validity and the resulting `expires_at`.

---

## Primary outcomes

1. Every B2B buyer — clinic procurement officer, dental lab purchasing manager, university supply officer — can request a price quote (from a cart or from a single product), see its lifecycle status, request revisions, and convert an accepted quote into a real order with their PO number and invoice-billing terms — entirely in Arabic or English from the customer surface.
2. Every individual customer (non-company) can also request a quote (e.g. a clinic owner registered as an individual), with the same state machine and contracts — the company-account features simply do not apply.
3. Companies can register on the platform, host multiple users (buyer-role + approver-role), organize themselves across branches, and enforce an internal approval flow where a buyer submits acceptance of a quote and a designated approver finalizes it before it converts to an order.
4. Admin commercial operators can author quotes against an existing customer cart or company-account request — setting line-item pricing (with the centralized pricing engine providing the baseline), discount terms, validity window, payment terms, and an optional message — then publish for customer review; revisions are versioned, every published version is preserved.
5. Acceptance triggers a single, traceable conversion to an order with the captured PO number, invoice-billing flag (so spec 012 issues an invoice on terms rather than waiting for prepayment), and a back-link to the originating quote and company account.
6. Backend persistence is in place for naming a quote as a "repeat-order template" — full template-management UI is Phase 1.5-c, but no schema migration is needed when that ships.
7. The data model, permissions surface, and contracts are designed so that a future multi-vendor expansion (Phase 2) can split a quote across vendors without rewriting the quote state machine, the company-account model, or the conversion contract.

---

## Roles and actors

| Actor | Surface | Permissions introduced by this spec |
|---|---|---|
| Customer (individual) | customer mobile app + web storefront | requests quotes, views own quotes, accepts / rejects own quotes, requests revisions |
| Buyer (company-account user) | customer surface, scoped to the company | requests quotes on behalf of company, submits acceptance for approval, requests revisions; cannot finalize acceptance without the approver step (when an approver exists for the company) |
| Approver (company-account user) | customer surface, scoped to the company | finalizes a buyer-submitted acceptance, may reject, may comment back to the buyer; sees every quote owned by the company |
| Company admin (company-account user) | customer surface, scoped to the company | manages company members + branches + designates approvers; one company admin per company minimum |
| Admin commercial operator (`quotes.author`) | admin web | drafts, revises, publishes quotes; sets line-item pricing + terms + validity; views every quote |
| Admin commercial reviewer (`quotes.review`) | admin web | reads every quote; cannot author or publish (read-only operator role for support / finance scenarios) |
| Super-admin | admin web | inherits everything |

Permission identifiers (declared by this spec; granted by spec 015 / 019 in their role models):
- `quotes.author` — author / revise / publish quotes
- `quotes.review` — read every quote (no writes)
- `companies.admin` — admin-side company-account administration (used in spec 019 for moderation; declared here)

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A clinic procurement officer requests a quote from their cart and accepts it (Priority: P1)

As a **clinic procurement officer (buyer role) representing a company in KSA**, I need to load my cart with the SKUs I want, request a price quote from that cart in Arabic, see the admin-authored quote with a clear validity date, optionally request a revision, then submit acceptance for my approver to finalize so that it converts to an order with our company PO number and invoice-billing terms — without ever leaving the customer surface or losing language quality.

**Why this priority**: This is the core B2B revenue path. Constitution Principle 9 makes B2B a V1 launch requirement, not a future afterthought. Without this loop, the company-account population the platform pitches to (clinics, labs, universities) cannot transact at scale.

**Independent Test**: Authenticated buyer in a company-account in KSA, Arabic locale, loads 5 SKUs into cart, requests a quote with a free-text message and PO number `PO-2026-0042`. An admin commercial operator authors the quote against the cart with line-item pricing, sets validity to 14 days, and publishes. The buyer reviews the quote, submits acceptance. The approver (a different user on the same company-account) finalizes the acceptance. An order is created with the PO number, invoice-billing flag set, and a back-link to the quote. Spec 011's order detail shows the order; spec 012's invoice can be issued on terms.

**Acceptance Scenarios**:

1. **Given** a company buyer in Arabic locale with a non-empty cart, **When** they submit "request quote" with a message + PO number, **Then** a quote is created in `requested` state, the company is captured, the cart contents are snapshotted into the quote, the buyer sees a localized confirmation with the new quote reference, and the cart is cleared (so subsequent shopping does not collide with the open quote).
2. **Given** a quote in `requested` state, **When** the admin commercial operator opens it and authors line-item pricing + payment terms + validity period, **Then** the quote moves to `drafted` (still customer-invisible), and a subsequent "publish" action moves it to `revised` if it has been published before, else moves it to `drafted → revised` only after the first revision (initial publish moves it to `revised` from `drafted` *or* keeps `drafted` until the customer is notified — see FR-008 / Edge Cases).
3. **Given** the customer receives the published quote, **When** they request a revision with a localized comment, **Then** the quote moves back to `drafted` for the operator (loop-back), the prior published version is preserved as a `QuoteVersion` (immutable), and a new version is being authored.
4. **Given** the quote is `revised` (currently published) and the customer (buyer) submits acceptance, **When** the company has an approver designated, **Then** the quote moves to `pending-approver` and the approver receives a localized notification on their preferred channel; if no approver is designated for the company (configurable per company), the buyer's acceptance is final.
5. **Given** the approver finalizes the acceptance, **When** the quote moves to `accepted`, **Then** an order is created with `po_number`, `invoice_billing` true, a snapshot of the quote line items, the company-account reference, and the buyer's user reference; the quote becomes immutable (terminal `accepted`).
6. **Given** any screen, validation message, push, email, SMS, or PDF produced by this flow, **When** rendered in Arabic, **Then** strings are editorial-grade, numerals + dates are locale-correct, and layout mirrors fully to RTL.

---

### User Story 2 — An individual customer (no company) requests a quote from a single product (Priority: P1)

As an **individual customer (e.g. a sole-practitioner dentist who is not part of a company-account)**, I need to request a quote from a single product page (e.g. a high-value piece of equipment), receive an admin-authored quote, and accept it directly (no approver step) so that I can place an order with terms negotiated, in either Arabic or English.

**Why this priority**: P1 because Constitution Principle 9 makes B2B "first-class" — but the platform's customer base also includes individual professionals who legitimately want quotes for high-value products without forming a company. Excluding them halves the quote feature's reach.

**Independent Test**: Individual customer (no company-account) in Egypt, English locale, opens a high-value product detail page, taps "Request quote", optionally edits the requested quantity, submits the request without a PO number. An admin authors and publishes the quote. The customer accepts it directly. An order is created without `po_number` (or with a customer-supplied one) and without the `invoice_billing` flag (since no company terms are in effect — payment proceeds normally per spec 010 checkout rules).

**Acceptance Scenarios**:

1. **Given** an authenticated individual customer (no company-account) on a product detail page, **When** they tap "Request quote" and submit with a quantity + optional message, **Then** a quote is created in `requested` state with a single line item; PO number is optional (not required for individuals).
2. **Given** the published quote, **When** the customer accepts, **Then** the quote moves directly to `accepted` (no approver step), an order is created without the invoice-billing flag, and the customer is routed into the standard spec 010 checkout to pay using the agreed line-item totals.
3. **Given** the customer is in `accepted` for a quote, **When** they later abandon the resulting checkout, **Then** the quote remains `accepted` (terminal); a new quote request must be made if they want re-pricing.

---

### User Story 3 — Admin commercial operator drafts and revises a quote (Priority: P1)

As an **admin commercial operator with `quotes.author`**, I need to open a `requested` quote, see the customer's cart / product context + company context (if applicable), set line-item pricing using the centralized pricing engine as a starting point, layer per-line discounts, set payment terms + validity window + optional internal note + customer-facing message, then publish — and revise on customer feedback while preserving every published version for audit.

**Why this priority**: Without an authoring surface, no quote ever gets out of `requested`. P1 to complete the round trip.

**Independent Test**: Operator with `quotes.author` opens a `requested` quote (from US1), sees the cart snapshot + company info; pricing engine returns base prices per SKU + applicable promotions + tax preview; operator overrides the price on one line with a 12% discount and a reason note; sets payment terms = "Net 30", validity = 14 days; publishes. The quote becomes customer-visible. Operator reverses a revision (e.g. typo) — every published version is retrievable from the `quote_versions` history.

**Acceptance Scenarios**:

1. **Given** a quote in `requested` state, **When** the operator opens the authoring view, **Then** they see the cart snapshot (or product line item for US2 origin), the customer / company context (name, market, professional verification state from spec 020 if applicable), the pricing-engine baseline per line, and any active promotion applicable to the buyer.
2. **Given** the operator overrides a line price below the pricing-engine baseline, **When** they save, **Then** the system requires a non-empty reason for each override (audited per FR-022), and the override + reason persist on the line-item snapshot of that quote version.
3. **Given** the operator publishes the quote, **When** publish completes, **Then** the customer is notified on their preferred channel + locale, the quote is now customer-visible, and a `QuoteVersion` row is written with the full line-item snapshot + the prices the customer will see.
4. **Given** the customer requests a revision, **When** the operator opens the quote, **Then** they see the previous published version side-by-side with the customer's comment; their authoring view is pre-filled with the previous version so changes are diff-able.
5. **Given** the operator publishes a second version, **When** publish completes, **Then** a second `QuoteVersion` row is written and the customer-visible state is the new version (the prior version remains in history).
6. **Given** the quote enters a terminal state (`accepted`, `rejected`, `expired`), **When** the operator opens it, **Then** the authoring view is read-only and clearly indicates the terminal state and reason.

---

### User Story 4 — Company-account administration (Priority: P2)

As a **company admin** for my company-account, I need to register the company, designate buyer + approver users, organize multiple branches (e.g. main clinic + 3 satellite clinics), set the optional approver workflow on or off, and add or remove members so that our procurement workflow matches our actual organization.

**Why this priority**: Without this surface, the buyer/approver roles in US1 cannot exist; however US2 (individual customer quotes) is testable without it, so P2 captures the business-importance ordering.

**Independent Test**: An authenticated customer registers a company-account named "ABC Dental Clinic LLC" with a tax ID, designates themselves as `companies.admin`, invites two more users by email (one assigned `buyer`, one `approver`), and creates two branches ("Main Clinic" and "North Branch"). Quote requests from buyers are routed to that company; the approver-required flag is configurable per company.

**Acceptance Scenarios**:

1. **Given** an authenticated customer with no existing company-account membership, **When** they submit company registration with name + tax ID + market + at least one address, **Then** a company-account is created, the registering user is assigned `companies.admin` + `buyer` for that company, and the company is in `pending-verification` state if the market requires admin verification (configurable per market) or `active` otherwise.
2. **Given** a company admin, **When** they invite a user by email + role (`buyer` or `approver` or `companies.admin`), **Then** an invitation is sent in the invitee's preferred locale; on accept, the invitee is bound to the company with the assigned role; on decline / expiry the invitation moves to its terminal state with no side effects.
3. **Given** a company admin, **When** they create a branch with name + address + optional contact phone, **Then** the branch is attached to the company; quotes and orders may reference the branch (default = company HQ if unspecified).
4. **Given** a company admin disables the approver-required flag for the company, **When** a buyer subsequently submits acceptance, **Then** the quote moves directly to `accepted` (no `pending-approver` interstitial state).
5. **Given** a company admin removes a user, **When** the user is currently the only approver and approver-required is on, **Then** the action is rejected with a clear error and the admin is prompted to either disable approver-required or designate a new approver first (FR-038).

---

### User Story 5 — Approval flow inside a company-account (Priority: P2)

As an **approver** for my company-account, I need to receive a notification when a buyer submits acceptance of a quote, see the quote's line items + the buyer's note + the validity window, and either finalize the acceptance (which converts to an order) or reject with a comment (which loops back to the buyer who may request another revision from admin).

**Why this priority**: P2 because US1 already covers the round trip with a designated approver; this story makes the approver-side surface a first-class workflow with its own listing + notification UX.

**Independent Test**: Buyer submits acceptance on a quote → approver gets a localized push + email + sees the quote in an "awaiting your approval" list with the buyer's note; approver finalizes → quote → `accepted` → order created. In a second case approver rejects with a comment → quote returns to `revised` and is open to the buyer to request another admin revision or close out.

**Acceptance Scenarios**:

1. **Given** an approver with at least one quote in `pending-approver`, **When** they open their approver list, **Then** they see every quote awaiting their approval with the buyer's name, branch, total, line-item summary, validity-remaining, and the buyer's note.
2. **Given** the approver finalizes acceptance, **When** the action completes, **Then** the quote moves to `accepted`, an order is created, and the buyer + the approver both receive a confirmation in their preferred locale.
3. **Given** the approver rejects with a comment, **When** the action completes, **Then** the quote returns to `revised` with an internal `approver_rejection_note`; the buyer is notified; the approver list no longer shows the quote; the buyer may request a new admin revision or accept again later (re-triggering approver review).
4. **Given** a quote in `pending-approver` whose validity expires while awaiting approver action, **When** the expiry job runs, **Then** the quote moves to `expired`, no order is created, and both buyer + approver are notified that the quote expired.

---

### User Story 6 — Quote-to-order conversion with PO + invoice-billing (Priority: P1)

As **the platform**, I need to convert an accepted quote into a real order in spec 011 with: the PO number captured at acceptance, an `invoice_billing` flag that signals spec 012 to issue an invoice on terms (not on prepayment), a snapshot of the agreed line items + totals (immutable on the order), the company-account reference, the buyer's user reference, and the originating quote's id — so that operations downstream (fulfillment, finance, support) can trace any company order back to the deal that priced it.

**Why this priority**: P1 because the quote system has no value without delivering on the conversion. Without a clean conversion contract, every downstream module (orders, invoices, support tickets, returns) re-implements the "is this from a quote?" question and the auditability story degrades.

**Independent Test**: A quote in `accepted` state automatically results in exactly one order being created (idempotent — replaying the conversion does not create a duplicate); the order references the quote and company; the order's invoice-billing flag is true (US1) or false (US2); spec 011's order detail surfaces the back-link; spec 012's invoice issuance respects the flag.

**Acceptance Scenarios**:

1. **Given** a quote in `pending-approver` finalized by an approver, **When** the conversion fires, **Then** exactly one order is created within the same transaction; the quote moves to `accepted`; the order references the quote-id + company-id + buyer-id; the order's `invoice_billing` flag matches the company's invoice-billing eligibility.
2. **Given** a US2 individual quote in `revised` accepted by the customer, **When** the conversion fires, **Then** an order is created with `invoice_billing=false` and no company reference; the customer is routed into spec 010 checkout to pay.
3. **Given** the conversion transaction fails midway (e.g. spec 011 rejects the order due to a stock revalidation race per spec 008), **When** the failure is observed, **Then** the quote stays in `revised` (or `pending-approver` for US1 if it was the approver step), the customer is shown a localized "we could not place the order — please try again" message, and an audit event captures the failure cause.
4. **Given** an order has been created from a quote, **When** that order is later cancelled per spec 011, **Then** the quote remains `accepted` (terminal); a new quote request is required if the customer wants the price re-instated.

---

### User Story 7 — Repeat-order template stub (Priority: P3)

As a **buyer**, I want to mark an accepted quote as a "repeat-order template" so that when Phase 1.5-c ships the template-management UI, the data is already there.

**Why this priority**: P3 because the data persistence is cheap and prevents a Phase 1.5 schema migration; the UI to manage templates is explicitly out of scope here per the implementation plan.

**Independent Test**: Buyer marks an accepted quote as a template with a name; the template is persisted with a back-reference to the quote and the company; subsequent reads return the template; the same quote cannot be re-saved as a template under the same name (uniqueness within company-account).

**Acceptance Scenarios**:

1. **Given** an accepted quote owned by a company-account, **When** a buyer with the company submits "save as repeat-order template" with a name, **Then** a `RepeatOrderTemplate` row is persisted referencing the quote-id + company-id + creating-user; no scheduling, no listing UI, no recurrence engine in V1.
2. **Given** the same quote, **When** a second save with a duplicate template name is attempted, **Then** the action is rejected with a localized "name already in use" error.
3. **Given** Phase 1.5-c subsequently ships, **When** the new template-listing endpoint runs, **Then** every template persisted in V1 is visible without a data migration.

---

### Edge Cases

- **Cart concurrency**: a buyer requests a quote from their cart, then another user on the same company adds an item to the buyer's cart between the request and the admin authoring. The quote MUST snapshot the cart at the moment of request — subsequent cart edits do not retroactively modify the quote.
- **Customer requests a quote, then their professional-verification status (spec 020) flips to `expired`** before the admin authors the quote. The admin authoring view MUST display a clear warning and the quote MAY still be authored — but the conversion to order will be gated by spec 020's eligibility query at conversion time, so the customer cannot end up with restricted SKUs they're no longer eligible for.
- **Quote validity expires** while the customer is in the middle of accepting (race between expiry job and accept call). The accept call MUST observe the expiry and reject with a clear localized message; no half-state where the customer thinks they accepted but the quote is `expired`.
- **An approver leaves the company between buyer-submit and approver-finalize**: because quotes are not bound to a specific approver (FR-028), the remaining approvers retain finalization rights with no quote-state change. If the departing approver was the *only* approver and `approver_required=true`, the quote MUST move back to `revised` and the buyer is notified.
- **Approver-required flag is toggled while a quote is in `pending-approver`**: turning it OFF MUST NOT auto-finalize the pending quote (avoids a buyer accidentally bypassing approver review by changing the flag mid-flight); the buyer must re-submit acceptance under the new policy.
- **Operator publishes a quote, then the customer's company is suspended (spec 019 admin action)**. The customer MUST be unable to accept; existing accepted quotes are unaffected (orders already exist).
- **Operator authors a quote whose lines no longer all exist in the catalog** (a SKU was archived between request and authoring). The authoring view MUST flag those lines explicitly; the operator may remove them, replace them, or reject the quote with a reason.
- **Operator overrides a line price below cost** — there is no automatic block (margin policy is admin-team scope), but the audit log MUST capture the override + the reason.
- **PO number reuse**: when `Company.unique_po_required=false` (default), a PO number reused across quotes for the same company MUST trigger a soft warning at buyer acceptance prompting confirmation, but the system commits on confirm. When `unique_po_required=true`, the system MUST hard-reject with `quote.po_already_used` — uniqueness scope is "all quotes ever for that company" (any state, terminal or not), enforced by a unique index. Quote-converted orders inherit the PO from the quote (back-linked); standalone non-quote orders are out of scope for PO uniqueness.
- **Quote across markets**: a buyer who changes their market-of-record between request and acceptance MUST trigger a quote-revalidation (the quote's market is captured at request time; if the customer's current market differs at acceptance, the quote is rejected with `quote.market_mismatch` and a new quote must be requested).
- **A quote arrives at a company with a verified professional-buyer role for restricted-SKU products** (spec 020 eligibility) — eligibility is evaluated **per buyer at acceptance** (the buyer who submits acceptance), not at quote-request time, because the buyer's verification status may change between events.
- **Rate of quote requests**: an individual or company spamming the system with quote requests MUST hit rate limits per spec 003's platform middleware; a denied request returns a localized message with retry-after.
- **Invitation expiry**: a company-admin invitation that goes unclaimed for 14 days expires; the company admin sees the invitation as `expired` and may resend.
- **Tax preview drift**: the pricing engine's tax preview at authoring time may differ from the tax computed at order conversion (e.g. tax rate change in between). The order's tax is authoritative; if the difference exceeds a per-market threshold, the conversion MUST surface the change before finalizing for both individual customer (US2) and approver (US1).

---

## Requirements *(mandatory)*

### Functional Requirements

#### Quote lifecycle and state model (Principle 24)

- **FR-001**: System MUST model each quote as a single entity with an explicit state machine over `requested → drafted → revised → (pending-approver) → (accepted | rejected | expired | withdrawn)`. Drafted is operator-only-visible; revised is customer-visible. `pending-approver` is reachable only from `revised` and only when the company-account has an approver designated and `approver_required=true`. Terminal states: `accepted`, `rejected`, `expired`, `withdrawn`.
- **FR-002**: Every state transition MUST have a defined trigger, allowed-actor set, and outcome — no implicit state changes.
- **FR-003**: Every published version of a quote MUST be preserved as an immutable `QuoteVersion` row; the customer-visible "current quote" is always the latest published version, but every prior version is retrievable for audit and customer reference.
- **FR-004**: System MUST be idempotent against duplicate accept attempts; a second accept-call after a successful first returns the original 200 response (per spec 003's `Idempotency-Key` middleware).
- **FR-005**: Customers MUST be able to withdraw a quote in any non-terminal state (`requested`, `drafted` → not visible to customers though; so really `revised`, `pending-approver`); withdrawal is a customer-initiated terminal transition.
- **FR-006**: Quote validity (default 14 days, configurable per market) MUST set `expires_at` at the moment the operator publishes the first version; revisions do not reset validity unless the operator explicitly extends it. When extension is chosen on a revision (`QuoteVersion.validity-extends=true`), the new `expires_at` MUST be computed as `(this revision's published_at + market.validity_days)` — i.e. the validity window restarts from the revision publish, not from the prior `expires_at`. When extension is NOT chosen (`validity-extends=false`), the existing `expires_at` is preserved unchanged. The audit log MUST record whether each revision extended validity and the resulting `expires_at`.
- **FR-007**: A scheduled job MUST move quotes whose `expires_at` has passed (and which are in any non-terminal state other than `accepted` already in flight) to `expired`, with audit entry attributing `system` as actor.
- **FR-008**: First publish of a quote MUST move it from `drafted` to `revised` (not introduce a separate "first-published" state); subsequent revisions cycle `revised → drafted (operator-only) → revised`.

#### Customer quote request

- **FR-009**: Customers MUST be able to request a quote from a non-empty cart (US1) or from a single product detail (US2) supplying an optional message + optional PO number (PO required only when the company-account has `po_required=true`).
- **FR-010**: At request time the system MUST snapshot the cart contents (sku, quantity, requested-line-note) into the quote; subsequent cart edits MUST NOT modify the quote.
- **FR-011**: At request time the system MUST capture the customer's market-of-record on the quote; quotes are scoped to a single market and cannot be cross-market accepted.
- **FR-012**: Customers MUST see, on the quote screen, the current state, the validity expiry, the latest published version's line items + totals, and any pending action (e.g., "Awaiting your approver", "Quote expired — request a new one").
- **FR-013**: Customers MUST be able to request a revision in any `revised` state with a localized comment (free text); the comment is preserved on the next `QuoteVersion`.

#### Admin quote authoring

- **FR-014**: Admin operators with `quotes.author` MUST have access to a queue of `requested` quotes filtered by market scope, with default sort oldest-first; SLA target same shape as spec 020 (decision/publish within 2 business days, configurable per market).
- **FR-015**: The admin authoring view MUST surface: the cart-or-product snapshot, the customer + company context (name, market, verification state from spec 020 if applicable), the pricing-engine (spec 007-a) baseline per line, applicable promotions, and a full prior-version diff when revising.
- **FR-016**: The admin operator MUST be able to override per-line price; below-baseline overrides MUST require a non-empty reason captured on the line-item snapshot.
- **FR-017**: The admin operator MUST be able to set: per-line discount, payment terms (free text + structured `terms_days` int for invoice billing), validity period (defaults to market policy), customer-facing message (locale-aware), internal note (operator-only).
- **FR-018**: Publishing MUST move the quote to `revised`, write a `QuoteVersion` snapshot, generate a bilingual (EN + AR) quote PDF for the version (one document per locale, persisted via spec 012's `IStorageService`-backed PDF infrastructure as `QuoteVersionDocument` rows), and trigger a customer notification through spec 025 in the customer's preferred locale with a link to the appropriate-locale PDF. PDFs are retrievable by buyer / approver / admin commercial operators via signed-URL download for the lifetime of the parent `QuoteVersion`.

#### Company accounts and B2B membership

- **FR-019**: System MUST model `Company` entities with: name, tax-id (per market), market-of-record, primary address, optional billing address, `approver_required` flag (default true; configurable), `po_required` flag (default false), `unique_po_required` flag (default false). When `unique_po_required=true`, the PO number MUST be unique across **all quotes ever owned by that company** (any state, terminal or non-terminal); enforced by a unique index `(company_id, po_number) WHERE po_number IS NOT NULL`. When `unique_po_required=false`, PO reuse triggers a soft warning at buyer acceptance.
- **FR-020**: System MUST model `CompanyMembership` linking a customer-account to a company with role ∈ {`companies.admin`, `buyer`, `approver`}. A user MAY hold multiple roles in one company. Multiple users MAY hold each role except a company MUST have at least one `companies.admin` at all times.
- **FR-021**: System MUST model `CompanyBranch` as an optional sub-entity of a company with name + address; quotes and orders MAY reference a branch (default = company HQ).
- **FR-022**: A company-account MUST be created by a self-registration flow (the registering user becomes the first `companies.admin` and `buyer`). Whether the new company starts in `pending-verification` or directly in `active` is governed by a per-market toggle `company_verification_required` (default **OFF** for both KSA and EG at V1 launch — companies are `active` immediately). Operations may flip the toggle ON post-launch via configuration without a code deploy. The toggle does NOT change spec 020's per-buyer professional-verification requirement, which remains independently enforced at acceptance time for restricted-SKU lines (FR-036).
- **FR-023**: Company admins MUST be able to invite users by email; invitations carry a 14-day TTL; on accept the invitee is bound to the company with the assigned role.
- **FR-024**: Removing the only `companies.admin` MUST be rejected (the surviving admin must designate another or delete the company entirely via `companies.admin` action).
- **FR-025**: Removing the only `approver` while `approver_required=true` MUST be rejected (FR-038 below); company admin must either disable `approver_required` or designate another approver first.
- **FR-026**: Suspending a company-account (admin action, defined in spec 019 — declared here) MUST prevent any new quote requests, prevent acceptance of any non-terminal quote, and notify all company members.

#### Approval flow

- **FR-027**: When a buyer submits acceptance of a `revised` quote, routing depends on the company's approver configuration:
  - If `approver_required=true` AND the company has ≥ 1 approver, the quote transitions to `pending-approver` and **every approver in the company** is notified simultaneously.
  - If `approver_required=true` AND the company has 0 approvers, acceptance is rejected with `quote.no_approver_available`; a localized message advises the buyer to ask the company admin to designate one.
  - If `approver_required=false`, the buyer's acceptance is final (no `pending-approver` interstitial).
- **FR-028**: Every approver of the company MUST be able to view every quote in `pending-approver` for that company in their approver queue, with the buyer's identity + branch + acceptance note + validity remaining; quotes are not bound to a specific approver.
- **FR-029**: Any approver MAY finalize (→ `accepted`) or reject (→ `revised`, with comment) a `pending-approver` quote — the first action wins. Concurrent actions are guarded by optimistic concurrency: the loser receives `quote.already_decided` with no side effects. Rejection writes an `approver_rejection_note` (locale-aware per FR-042) that the buyer sees on next quote view, plus the identity of the rejecting approver.
- **FR-030**: Because quotes are not bound to a specific approver (FR-028), an approver leaving the company while a quote is in `pending-approver` requires **no re-routing**; the remaining approvers retain visibility and finalization rights. If the departing approver was the *only* approver and `approver_required=true`, the quote transitions back to `revised` and the buyer is notified to ask the company admin to designate a new approver before resubmitting acceptance.
- **FR-031**: A toggle of `approver_required` from `true` to `false` MUST NOT auto-finalize quotes already in `pending-approver` (Edge Case); pending quotes return to `revised` and require a fresh acceptance under the new policy.

#### Quote-to-order conversion

- **FR-032**: When a quote enters `accepted` state, exactly one order MUST be created in spec 011 within the same transaction (atomic — quote-acceptance and order-creation succeed or fail together); the order carries the quote-id, company-id (nullable), buyer-id, PO number, `invoice_billing` flag, and a snapshot of the agreed line items + totals.
- **FR-033**: For US1 (company-account with `invoice_billing` enabled), the order's `invoice_billing` flag MUST be true and spec 012 issues a tax invoice on terms (Net X days per the quote's `terms_days`); for US2 (individual), `invoice_billing` MUST be false and spec 010 checkout flow runs as normal.
- **FR-034**: The conversion MUST be idempotent — replaying the acceptance does not create a duplicate order (per spec 003 `Idempotency-Key`).
- **FR-035**: If the spec 011 order-creation rejects (e.g. stock revalidation per spec 008), the quote MUST stay in its prior state, an audit event MUST capture the failure cause, and the customer + approver (if applicable) MUST be informed with a localized message.
- **FR-036**: Spec 020's `ICustomerVerificationEligibilityQuery` MUST be invoked at acceptance time (not at request time) for any restricted SKU on the quote; failure results in `quote.eligibility_required` rejection of the acceptance.

#### Repeat-order template (backend stubs)

- **FR-037**: A buyer MUST be able to mark an accepted quote as a `RepeatOrderTemplate` with a name; the persisted row carries quote-id, company-id (nullable), creating-user-id, name, created-at; uniqueness `(company_id, name)` enforced (or `(user_id, name)` for individual customers).
- **FR-038**: No template-listing UI, scheduling, recurrence engine, or auto-reorder behavior is delivered in V1; the backend persistence is the deliverable.

#### Audit (Principle 25)

- **FR-039**: Every state transition + every line-item override + every invitation event + every company-membership change MUST emit an audit event via spec 003's `IAuditEventPublisher` capturing actor (or `system`), timestamp, prior state, new state, and structured metadata.
- **FR-040**: Below-baseline price overrides MUST capture the override reason in the audit metadata for finance review.

#### Bilingual + RTL (Principle 4)

- **FR-041**: All customer-facing screens, validation messages, push notifications, emails, SMS, PDFs, and reviewer reason renderings MUST be available in Arabic and English with editorial-grade Arabic; layouts mirror to RTL.
- **FR-042**: Admin operator decision reasons (price overrides, terms, customer-facing messages) follow spec 020's pattern: structured `{ en?, ar? }` reason / message bodies — at least one locale required; both preserved in audit.

#### Notifications integration (Principle 19)

- **FR-043**: Spec 025 subscribes to domain events `QuoteRequested`, `QuotePublished`, `QuoteAccepted`, `QuoteRejected`, `QuoteExpired`, `QuoteWithdrawn`, `QuotePendingApprover`, `QuoteApproverRejected`, `CompanyInvitationSent`, `CompanyInvitationAccepted`, `CompanyInvitationExpired`. Quote state writes MUST NOT block on notification success.

#### Multi-vendor readiness (Principle 6)

- **FR-044**: The quote entity, line-item model, conversion contract, and company-account model MUST reserve a future `vendor_id` dimension; V1 always sets vendor_id to null. The state machine, the eligibility integration, and the customer flow MUST NOT change when a future spec adds vendor scoping.

#### Rate limiting + operational safeguards

- **FR-045**: Quote-request submissions MUST be rate-limited per customer + per company-account (defaults: 10 quote requests / hour / customer; 50 quote requests / hour / company; tunable per market). Excess requests get `quote.rate_limit_exceeded` with localized retry-after.
- **FR-046**: When a customer's market-of-record changes (FR from spec 004) while a non-terminal quote is open, the quote MUST move to `withdrawn` with reason `customer_market_changed`; a new quote must be requested for the new market.

### Key Entities

- **Quote**: The top-level lifecycle entity. Carries: id, customer-id, company-id (nullable), branch-id (nullable), market-of-record, state, requested-at, expires-at, terminal-at, terminal-reason, originating-cart-snapshot (jsonb) or originating-product-id, current-version-id (FK to QuoteVersion), po_number (nullable), invoice_billing (bool), customer-supplied-message (jsonb `{en?, ar?}`), internal-note (operator-only text), approver-rejection-note (nullable text).
- **QuoteVersion**: Immutable snapshot of the quote at each publish. Carries: id, quote-id, version-number (1, 2, …), authored-by (admin user-id), published-at, line-items (jsonb array of `{ sku, qty, baseline-price, override-price, override-reason, line-discount, line-tax-preview }`), terms-text (jsonb `{en, ar}`), terms-days (int), validity-extends (bool), totals-summary (subtotal, total discount, total tax preview, grand total).
- **QuoteVersionDocument**: Per-locale PDF rendering of a `QuoteVersion`. Carries: id, quote-version-id, locale ∈ {`en`, `ar`}, storage-key (via spec 012's `IStorageService`-backed PDF infrastructure), content-type (`application/pdf`), generated-at. Two rows per version (one EN, one AR). Retrievable by buyer / approver / admin commercial operators via signed-URL download for the lifetime of the parent `QuoteVersion`.
- **Company**: A company-account. Carries: id, name (jsonb `{en, ar}` — companies often have bilingual legal names), tax-id, market-of-record, primary-address, billing-address (nullable), approver-required (bool, default true), po-required (bool, default false), unique-po-required (bool, default false), invoice-billing-eligible (bool — true means the company can use Net-X terms), state ∈ {`active`, `pending-verification`, `suspended`, `closed`}, created-at.
- **CompanyMembership**: Edge between customer-account and company with one role per row (multi-role users have multiple rows). Carries: id, company-id, user-id, role ∈ {`companies.admin`, `buyer`, `approver`}, joined-at.
- **CompanyBranch**: Sub-entity of a company. Carries: id, company-id, name (jsonb `{en, ar}`), address, contact-phone (nullable).
- **CompanyInvitation**: An outstanding invite. Carries: id, company-id, invited-by (user-id), invited-email, target-role, token (opaque), state ∈ {`pending`, `accepted`, `declined`, `expired`}, sent-at, expires-at (sent-at + 14 days).
- **RepeatOrderTemplate**: V1 backend stub. Carries: id, company-id (nullable), user-id, source-quote-id, name (jsonb `{en?, ar?}`), created-at, created-by.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A B2B buyer can complete the request → published-quote → buyer-submit → approver-finalize → order-created loop end-to-end in under 5 admin authoring days from request (under the default 2-business-day SLA per FR-014, plus normal customer + approver response time), with all state transitions audited and reproducible from `audit_log_entries` alone.
- **SC-002**: An individual (US2) customer can complete request → published-quote → accept → order in under 3 admin authoring days; backend write paths (request, publish, accept, conversion) each return p95 ≤ 1500 ms (excluding pricing-engine + storage IO).
- **SC-003**: 100% of accepted quotes produce exactly one order; replaying the accept call against the same idempotency key produces the same response and no duplicate order — verified by integration tests covering 100 simulated parallel accept attempts.
- **SC-004**: 100% of below-baseline price overrides capture an audit event with actor, timestamp, sku, baseline-price, override-price, and override-reason — verifiable by a finance audit script that replays the audit log.
- **SC-005**: 100% of customer-facing strings, decision-reason rendering, validation errors, push / email / SMS templates, and PDF renderings produced by this spec pass the Arabic editorial review and the RTL visual sweep (no LTR-mirrored screens, no clipped labels, no machine-translated text).
- **SC-006**: Quotes whose validity expires automatically transition to `expired` within one scheduled-job interval after `expires_at`; no quote remains in a non-terminal state past its expiry by more than a job interval.
- **SC-007**: Quote-to-order conversion is atomic — across 100 simulated conversions where the spec 011 order-creation deliberately fails 30%, the quote ends up in its prior state (no half-state) for every failure, and an audit event captures the failure cause.
- **SC-008**: Restricted-SKU eligibility (spec 020) is correctly enforced at acceptance time — across a synthetic matrix `(quote line restricted × buyer verification state × buyer market)` the eligibility decision agrees with spec 020's `ICustomerVerificationEligibilityQuery` 100% of the time.
- **SC-009**: A company-account with two approvers receives parallel notifications on every `pending-approver` quote; either approver may finalize, the first action commits and the second receives `quote.already_decided` with no side effects (verified across 100 simulated parallel finalize attempts). When one approver leaves the company, in-flight quotes remain finalizable by the surviving approver(s) with no quote-state change; the only-approver-leaves case correctly transitions the quote back to `revised` and notifies the buyer.
- **SC-010**: Rate-limit defaults block a 100-quote-in-1-hour burst from a single customer or company-account with localized retry-after, while allowing the configured cap (10 / customer / hour, 50 / company / hour) through cleanly.

---

## Assumptions

- Spec 004 (`identity-and-access`) is at DoD on `main`, providing customer-account primitives, RBAC, and the platform `Idempotency-Key` middleware.
- Spec 005 (`catalog`) is available for cart snapshotting + product detail context.
- Spec 007-a (`pricing-and-tax-engine`) provides the baseline-price + tax-preview API consumed by the admin authoring view (FR-015); promotion stacking rules are owned by 007-a.
- Spec 010 (`checkout`) is the path for individual-customer (US2) post-acceptance payment.
- Spec 011 (`orders`) accepts the conversion contract documented in `/plan` (back-link from order to quote, snapshot of line items, invoice-billing flag).
- Spec 012 (`tax-invoices`) honors `invoice_billing` to issue invoices on terms.
- Spec 015 (`admin-foundation`) ships the admin shell + RBAC; spec 018 (`admin-orders`) is independent of this spec but the admin authoring view is owned by 015's module composition.
- Spec 019 (`admin-customers`) eventually adds the company-suspend admin action; the suspend-quote-effect is declared here (FR-026) and consumed there.
- Spec 020 (`verification`) provides `ICustomerVerificationEligibilityQuery` for restricted-SKU gating at acceptance.
- Spec 023 (`support-tickets`) eventually links tickets to quotes; this spec exposes the quote id as a ticket-linkable subject.
- Spec 025 (`notifications`) is the channel for every customer-facing event listed in FR-043.
- Spec 1.5-c (`b2b-reorder-templates`) consumes the `RepeatOrderTemplate` rows shipped here without schema migration.
- B2B quote pricing currency follows the customer's market-of-record; cross-market quoting is out of scope (FR-011 / FR-046).
- Phase 2 multi-vendor is reserved (FR-044) but not built; the quote does not split across vendors in V1.
- No external CRM / ERP integration is in scope for V1; the quote-to-order back-link + audit log are the system of record.
- Tax invoicing for quote-converted orders follows spec 012 (Egypt + KSA tax requirements).
- Quote PDFs are **in scope for V1** (FR-018, Key entity `QuoteVersionDocument`); each `QuoteVersion` publish renders one EN PDF and one AR PDF via spec 012's PDF infrastructure, stored via the platform `IStorageService`, downloadable by the buyer / approver / admin commercial operators.
