# Feature Specification: Professional Verification

**Feature Branch**: `phase_1D_creating_specs` (working) · target merge branch: `020-verification`
**Created**: 2026-04-28
**Status**: Draft
**Constitution**: v1.0.0
**Phase**: 1D (Business Modules · Milestone 7)
**Depends on**: 004 `identity-and-access` at DoD; 015 `admin-foundation` contract merged to `main`
**Consumed by**: 005 `catalog`, 009 `cart`, 010 `checkout` (restricted-product eligibility hook); 025 `notifications` (renewal-reminder trigger)
**Input**: User description: "Phase 1D, spec 020 — verification. Submission + admin review queue + approve / reject / request-info + expiry + audit. Verification entity with explicit state machine; customer submission with profession, license number, and document uploads via storage; admin review queue with filters and decisioning that requires reasoning; expiry tracking with renewal-reminder trigger consumed by 025; restricted-product eligibility hook consumed by 005 / 009 / 010; market-aware fields capturing EG vs KSA regulator differences; full audit trail per decision; bilingual AR + EN end-to-end; multi-vendor-ready."

## Clarifications

### Session 2026-04-28

- Q: Document retention after a verification reaches a terminal state (`rejected`, `expired`, `revoked`, `superseded`, `void`) — how long are documents retained and who may access them? → A: Retain for a market-configured window (default KSA 24 months / EG 36 months) after terminal state. Access is reviewer-only via an audited "open historical document" action. Documents are auto-purged once the window elapses; metadata, reasons, and audit entries are preserved indefinitely.
- Q: External regulator integration scope for V1 (SCFHS for KSA, dental syndicate for EG) — assistive lookups, auto-decisioning, or none? → A: V1 is purely manual reviewer review of customer-supplied evidence. No external regulator API calls. The data model and reviewer UI MUST leave room for a future assistive-lookup panel (Phase 1.5+) without schema or contract change.
- Q: Per-submission document count and aggregate size limits? → A: Up to 5 documents per submission, max 10 MB per document, max 25 MB aggregate. Per-market policy MAY tighten these limits but MUST NOT raise them above the platform ceiling.
- Q: Reviewer SLA target — at what age does a `submitted` or `info-requested` verification count as breached? → A: Target decision (approve / reject / info-request) within **2 business days** of submission. Warning indicator surfaces at **1 business day**, breach indicator at **2 business days**. Business days follow the market's local working calendar (Sun–Thu for both KSA and EG). The platform surfaces these signals; it does not auto-decide aged requests.
- Q: PII access scope — beyond verification reviewers, who may see uploaded documents and license-number values? → A: Documents are reviewer-only (`verification.review`; terminal-state access via the audited "open historical document" action). License-number values are visible to reviewers and to holders of a new `verification.read_pii` permission (granted to the customer-account admin role defined in spec 019). Super-admin sees everything. Support agents (spec 023) see only the verification **state** and a **decision reason summary** — never the raw license number or documents; they MUST escalate to a reviewer for evidence access. Every read of documents or license-number values via these permissions MUST be recorded in the audit log.
- Q: Customer market-of-record change — what happens to in-flight non-terminal verifications? → A: They are voided with reason `customer_market_changed` (not preserved as reviewable items in the queue under the original market). Active `approved` verifications are separately superseded. The customer must open a fresh submission for the new market. Voided rows' `schema_version` + `restriction_policy_snapshot` remain attached to the audit history for traceability. Resolves an inconsistency between the prior edge-case wording (which read as "in-flight stays alive") and the implementation intent (research §R6 / tasks T110).

---

## Primary outcomes

1. Every dental professional — dentist, clinic buyer, lab technician, dental student — can submit a verification request from the customer surface in Arabic or English, attach the documents their market requires, see the current state and reason of their request, and receive notification when the decision lands or when their verification is about to expire.
2. Admin reviewers can work a single, filterable, market-aware queue; open a submission; demand more information, approve, or reject with required reasoning; and trust that every decision is captured in the audit log with the actor, timestamp, reason, and prior state.
3. Catalog, cart, and checkout (specs 005 / 009 / 010) can ask one canonical question — "is this customer currently eligible to purchase this restricted product?" — and receive a deterministic, market-aware answer without needing to know how verification works internally.
4. Verification expiry is tracked centrally; renewal reminders fire through the notification module (spec 025) on a defined cadence; and an expired verification automatically removes purchase eligibility for restricted products without an admin having to act.
5. The data model, market configuration, and admin role boundaries are designed so that future multi-vendor expansion (Phase 2) can layer vendor-scoped reviewers on top without rewriting the verification schema or its state machine.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A dental professional submits a verification request and is approved (Priority: P1)

As a **dentist, dental lab technician, clinic buyer, or dental student in KSA or Egypt**, I need to submit my professional credentials — profession, license / registration number, and supporting documents — from my customer account in Arabic or English, track the status of my request, and be told clearly when I am approved (and what I unlocked), or what is missing — so that I can purchase the restricted dental products that match my profession and market without going outside the platform.

**Why this priority**: This is the gateway to the restricted-product revenue path. Without a working customer-side submission and approval round-trip, restricted catalog SKUs (Principle 8) cannot be sold to anyone. Every other story in this spec depends on this loop existing end-to-end.

**Independent Test**: Fresh authenticated KSA dentist on the mobile app in Arabic locale submits a verification request with profession, KSA SCFHS license number, and a license document image. An admin reviewer (Story 2) approves it. The customer receives a localized notification, the verification appears in their account as Approved with an expiry date, and a restricted SKU on the storefront becomes purchasable for them and remains restricted for an unverified control account.

**Acceptance Scenarios**:

1. **Given** an authenticated customer in Arabic locale with no prior verification, **When** they open the verification flow and submit profession + KSA license number + a valid document, **Then** the request is created in `submitted` state with a unique reference visible to them, every label and validation error is editorial-grade Arabic with mirrored RTL layout, and the customer sees an in-app status of "submitted — under review".
2. **Given** an authenticated customer in English locale in Egypt, **When** they submit a request with profession + EG syndicate registration number + supporting document, **Then** the request is created with EG-specific required fields satisfied and the document is stored via the platform's storage abstraction, not embedded in any database row.
3. **Given** the customer has a request in `info-requested` state with a reviewer's reason "license image is unreadable", **When** they open the request, **Then** the reason is displayed in their locale, they can upload a new document or correct the field, and resubmission moves the request back to `in-review` (not `submitted`) and preserves the original submission timestamp.
4. **Given** the request is in `approved` state, **When** the customer adds a restricted product to their cart, **Then** the add-to-cart succeeds and the storefront eligibility hook (Story 5) returns "eligible" deterministically.
5. **Given** the customer is in `rejected` state with reason "license number does not match issuing authority", **When** they view the request, **Then** they see the reason in their locale, the prior submission's documents are no longer accessible to them, and they can open a new request only after a market-configured cool-down period.
6. **Given** any screen, OTP, email, push notification, or PDF produced by this flow, **When** rendered in Arabic, **Then** strings are editorial-grade (not machine-translated), numerals and dates are locale-correct, and the layout is fully mirrored to RTL.

---

### User Story 2 — An admin reviewer works the verification queue and decides a request with a reason (Priority: P1)

As a **verification reviewer (admin role)**, I need to open a single market-aware, filterable queue of pending verifications, inspect each submission with documents and customer context, and either approve, reject, or request more information — always entering a reason that is recorded in the audit log — so that the platform stays compliant with KSA and Egypt regulator expectations and every restricted-purchase eligibility decision is traceable to a named human.

**Why this priority**: Without a reviewer surface, no submission ever exits `submitted` state and customers can never become eligible. This is the second half of the P1 loop.

**Independent Test**: A reviewer with the verification-reviewer role logs into the admin web app, opens the queue filtered to KSA, sorts by oldest, opens the submission from Story 1, approves it with the reason "license verified against SCFHS register", and the audit log records actor, timestamp, prior state, new state, and reason.

**Acceptance Scenarios**:

1. **Given** the reviewer has the verification-reviewer permission, **When** they open the verification module, **Then** they see a queue defaulting to `submitted` and `info-requested` items in their assigned markets, and they can filter by market, profession, state, age, and free-text license number.
2. **Given** the reviewer opens a submission, **When** the detail loads, **Then** they see the customer's identity context (name, market, account age), the submission fields, every uploaded document rendered inline or downloadable, the full state-transition history, and the prior decision reasons if any.
3. **Given** the reviewer attempts to approve, reject, or request-info, **When** they submit the action without entering a reason, **Then** the action is rejected client-side and server-side with a clear localized validation error.
4. **Given** the reviewer approves a submission, **When** the action completes, **Then** the request moves to `approved`, the expiry date is set per market policy (Story 4), the audit log captures actor + timestamp + prior state + new state + reason, and the customer is notified through their preferred channel (push / email / SMS) in their locale.
5. **Given** the reviewer requests more info with reason "please upload the back side of your license", **When** the action completes, **Then** the request moves to `info-requested`, the reason is visible to the customer in their locale, and the customer is notified.
6. **Given** the reviewer rejects a submission, **When** the action completes, **Then** the request moves to `rejected`, no eligibility is granted, the customer is notified with the reason, and the request becomes immutable except for re-opening through a new submission after the cool-down.
7. **Given** two reviewers open the same submission concurrently, **When** the second reviewer attempts to act on it after the first has already decided it, **Then** the second action is rejected with a "request already decided" error and no double-decision is written to the audit log.

---

### User Story 3 — Catalog, cart, and checkout deterministically gate restricted products by verification state (Priority: P1)

As a **downstream module (catalog 005, cart 009, checkout 010)**, I need to ask one canonical question — "for this customer, in this market, is this restricted product eligible right now?" — and get a deterministic, low-latency answer that already accounts for verification state, expiry, market, and product restriction policy — so that I never need to reimplement verification logic and so that storefront, cart, and checkout always agree on what is purchasable.

**Why this priority**: Principle 8 (restricted products) is constitutional. If catalog and checkout disagree, customers can add a product to their cart and have it rejected at payment — a launch-blocker UX failure. The eligibility hook is what enforces consistency across all three surfaces.

**Independent Test**: A test customer with `approved` verification for profession "dentist" in KSA queries the eligibility hook for a SKU restricted to dentists in KSA → "eligible". The same customer queries the same SKU after their verification is force-expired → "not eligible: verification expired". An unverified customer queries any restricted SKU → "not eligible: verification required".

**Acceptance Scenarios**:

1. **Given** a customer with `approved` non-expired verification matching the product's required profession and market, **When** any module calls the eligibility hook for that customer + SKU, **Then** the hook returns `eligible` with a stable, machine-readable reason code.
2. **Given** a customer with `approved` but expired verification, **When** any module calls the hook, **Then** the hook returns `not_eligible` with reason code `verification_expired` and a localized message key.
3. **Given** a customer in any non-`approved` state (`submitted`, `in-review`, `info-requested`, `rejected`, `expired`, none), **When** any module calls the hook, **Then** the hook returns `not_eligible` with the corresponding reason code so the storefront can show the right localized message and CTA.
4. **Given** a product is restricted in KSA but unrestricted in Egypt, **When** the hook is called for an Egyptian customer with no verification, **Then** the hook returns `eligible` because the restriction does not apply in their market.
5. **Given** the product is unrestricted in any market, **When** the hook is called for any customer, **Then** the hook returns `eligible` without consulting verification state.
6. **Given** the hook is called repeatedly during a single browse / cart / checkout session, **When** verification state has not changed, **Then** the answer is consistent and fast enough to not noticeably degrade the calling surface (see SC-004).

---

### User Story 4 — Verification expiry tracking and renewal reminders (Priority: P2)

As a **verified professional**, I need to be reminded before my verification expires, see clearly in my account when it expires, and be able to submit a renewal early so I do not lose my purchase eligibility unexpectedly. As a **platform**, I need expiry to remove eligibility automatically, with no admin action required.

**Why this priority**: Without expiry handling, verifications accumulate stale approvals that no longer reflect regulator status. Without renewal reminders, customers are surprised at checkout. Both are correctness and trust failures, but neither blocks the launch of the P1 loop.

**Independent Test**: An approved verification with a synthetic expiry of "yesterday" is moved to `expired` by the daily expiry job. The eligibility hook returns `not_eligible: verification_expired`. Separately, an approved verification with expiry "in 30 days" triggers a renewal reminder via the notifications module on the configured cadence; a renewal submission can be made by the customer while the existing approval is still active and is auto-linked to the prior verification.

**Acceptance Scenarios**:

1. **Given** an approved verification whose expiry has passed, **When** the daily expiry job runs, **Then** the verification transitions to `expired`, the audit log records `system` as the actor with reason `expiry_reached`, and downstream eligibility immediately returns `not_eligible: verification_expired`.
2. **Given** an approved verification whose expiry is within the configured reminder window (default: 30 days, 14 days, 7 days, 1 day before expiry), **When** the reminder job runs on each window boundary, **Then** spec 025 emits a localized renewal-reminder notification on the customer's preferred channel, and a duplicate is not sent for the same window.
3. **Given** an approved verification within the renewal window, **When** the customer opens the verification module, **Then** they see a clearly labeled "renew now" entry point and can submit a new verification without losing their current approved state until the new one is decided.
4. **Given** a customer submits a renewal while the prior approval is still active, **When** the renewal is approved, **Then** the new expiry replaces the old one, the prior approval is closed with state `superseded`, and the audit log links the two verifications.
5. **Given** a customer submits a renewal while the prior approval is still active, **When** the renewal is rejected, **Then** the prior approval remains intact until its original expiry — a failed renewal does not retroactively revoke an active approval.

---

### User Story 5 — Market-aware required fields capture EG vs KSA regulator differences (Priority: P2)

As a **customer in KSA**, I need to provide the fields and documents my regulator (e.g. SCFHS for healthcare practitioners) requires. As a **customer in Egypt**, I need to provide the fields my regulator (e.g. dental syndicate) requires — which are not the same. As a **platform operator**, I need to be able to update those required fields per market without a code change.

**Why this priority**: Without market-aware fields, every customer is forced through a one-size-fits-all form that either over-collects (a privacy and trust failure) or under-collects (a compliance failure). It is launch-relevant but does not block the P1 loop because a single conservative superset can ship first if needed.

**Independent Test**: A KSA customer sees the KSA-specific required fields (license number format + issuer + document type), and an EG customer sees the EG-specific required fields, both rendered correctly in their locale with correct validation. Switching the customer's market in a test fixture immediately changes which fields are required for a new submission.

**Acceptance Scenarios**:

1. **Given** the customer's market is KSA, **When** they open a new verification submission, **Then** the form shows the KSA-required fields and rejects submissions missing any of them with localized validation messages.
2. **Given** the customer's market is EG, **When** they open a new verification submission, **Then** the form shows the EG-required fields and validates per the EG schema.
3. **Given** an operator updates the market configuration to add a new required field for KSA, **When** a new KSA customer opens the submission form, **Then** the new field appears without a code deploy; existing submitted-but-undecided requests do not retroactively become invalid.
4. **Given** a reviewer opens a request, **When** they view the submission detail, **Then** the displayed schema and labels match the market the customer was in at submission time, not the current market config (so reviewers see what the customer saw).

---

### User Story 6 — A reviewer reverses an approval after a regulator escalation (Priority: P3)

As a **senior verification reviewer**, I need to be able to revoke an active approval when new information shows the customer no longer qualifies (e.g. license revoked by the regulator), with a required reason and full audit, so that the platform reflects the regulator's current state without waiting for the natural expiry.

**Why this priority**: This is a real operational need but rare. It must exist for compliance defensibility but is not required for the launch of the P1 loop.

**Independent Test**: A senior reviewer opens an `approved` verification, executes a "revoke" action with a required reason, and the verification transitions to `revoked`. The eligibility hook now returns `not_eligible: verification_revoked`. The audit log captures actor + timestamp + prior state + new state + reason + supporting evidence reference.

**Acceptance Scenarios**:

1. **Given** an active `approved` verification, **When** a reviewer with the `verification.revoke` permission executes a revoke action with a required reason, **Then** the verification transitions to `revoked`, the customer is notified in their locale with the reason, and downstream eligibility immediately returns `not_eligible: verification_revoked`.
2. **Given** the same action attempted by a reviewer without the `verification.revoke` permission, **When** they submit, **Then** the action is denied with a 403-equivalent error and the attempt is recorded in the audit log.
3. **Given** a verification has been revoked, **When** the customer attempts to submit a new verification, **Then** they may do so without the cool-down that applies after a `rejected` state, because revocation reflects external regulator status, not application defects.

---

### Edge Cases

- A customer submits, then the underlying account is locked or deleted by spec 004 admin actions — the verification request must be moved to a terminal `void` state, eligibility immediately returns `not_eligible: account_inactive`, and reviewers no longer see it in the active queue.
- A customer's market is changed by an admin (per spec 004) after submission — any in-flight non-terminal verification (`submitted`, `in-review`, `info-requested`) is voided with reason `customer_market_changed`; any existing approval is moved to `superseded`; the customer must submit a new verification for the new market. The voided request's snapshotted schema (`schema_version` + `restriction_policy_snapshot`) remains attached to the audit history for traceability, but the customer cannot resubmit against the voided row — they open a fresh submission.
- An uploaded document exceeds size or fails antivirus scan at the storage layer — the upload is rejected with a localized error before the request can be submitted; the request itself is not created.
- A customer attempts to upload a document type that is not on the allow-list (e.g. an executable masquerading as a PDF) — rejected at the storage abstraction layer; surfaced as a localized validation error.
- The notification provider is degraded — decisions still apply and the audit log is still written; the failed notification is queued for retry per spec 025's delivery-log + dead-letter behavior. Verification state never depends on notification success.
- The reminder job runs after a window has already passed (e.g. due to outage) — it must not flood the customer with multiple back-windowed reminders; only the closest unfired window is sent, and an audit note explains the skip.
- A reviewer marks a request `approved` then immediately closes the browser — the action is recorded transactionally; refreshing the queue does not show a duplicate or a half-applied state.
- A customer in `rejected` state attempts to resubmit before the cool-down — the new submission is blocked with a localized message stating when they may try again.
- A SKU is restricted in EG but not KSA, and a customer with EG-issued verification travels to KSA and changes their market to KSA — the cross-market verification does NOT confer KSA eligibility; the customer must complete a KSA submission.
- All reviewers in a market are out of office and the queue ages past the SLA target — the platform must surface a queue-age signal in the admin module so a manager can reassign; verifications are not auto-decided.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Lifecycle and state model (Principle 24)

- **FR-001**: System MUST model each verification as a single entity with an explicit state machine over `submitted → in-review → (approved | rejected | info-requested) → (info-requested ↔ in-review)`, plus the terminal/derived states `expired`, `revoked`, `superseded`, and `void`.
- **FR-002**: System MUST enforce that every state transition has a defined trigger (customer action, reviewer action, system job), an allowed actor (customer, reviewer-with-permission-X, system), and an outcome — no implicit state changes.
- **FR-003**: System MUST reject any attempt to transition from a terminal state (`rejected`, `expired`, `revoked`, `superseded`, `void`) into a non-terminal state — a new submission is required.
- **FR-004**: System MUST be idempotent against duplicate decision attempts: if two reviewers act concurrently, only the first commits and the second receives a clear "already decided" error.

#### Customer submission

- **FR-005**: Customers MUST be able to submit a verification request from the customer surface (mobile app + web storefront) in Arabic or English, supplying profession, market-required identifier (e.g. license / registration number), and one or more supporting documents.
- **FR-006**: Documents MUST be uploaded through the platform's storage abstraction, never embedded in database rows, and MUST be subject to size limits, content-type allow-list, and antivirus / malware scanning before the verification can be submitted. Platform ceilings: **maximum 5 documents per submission, 10 MB per document, 25 MB aggregate**. Per-market policy MAY tighten these limits but MUST NOT raise them above the platform ceiling. Submissions exceeding any limit MUST be rejected with a localized validation error before the verification entity is created.
- **FR-006a**: Verification documents MUST be retained for a per-market configurable window after the verification enters any terminal state (`rejected`, `expired`, `revoked`, `superseded`, `void`); defaults: 24 months for KSA, 36 months for EG. After the window elapses, documents MUST be purged from the storage layer; the verification entity, state-transition history, decision reasons, and `audit_log_entries` MUST be preserved indefinitely.
- **FR-006b**: Access to documents on a verification in any terminal state MUST require the `verification.review` permission and MUST be exposed only through an explicit "open historical document" action; every such access MUST be recorded in the audit log with actor, timestamp, verification reference, and document reference. Customers MUST NOT have access to documents on a terminal verification.
- **FR-007**: Customers MUST see, on the verification screen, the current state, the latest reviewer reason (if any), the expiry date if approved, and any required next action (e.g. "upload missing document", "wait for review").
- **FR-008**: After a `rejected` decision, customers MUST be unable to open a new submission until a market-configured cool-down has passed (default 7 days, configurable per market).
- **FR-009**: After a `revoked` decision, customers MUST be allowed to submit a new request immediately (no cool-down), since revocation reflects external status, not customer defect.
- **FR-010**: Customers MUST be able to submit a renewal while their current approval is still active and within the configured renewal window; the prior approval remains active until the renewal is decided.

#### Admin reviewer queue

- **FR-011**: Admin reviewers MUST have access to a queue scoped by their assigned markets and the `verification.review` permission, defaulting to non-terminal states (`submitted`, `in-review`, `info-requested`).
- **FR-012**: The queue MUST support filtering by market, profession, state, submission age, and free-text search on the regulator identifier; sorting MUST default to oldest-first to bound queue age.
- **FR-013**: The submission detail view MUST display: the customer's identity context (name, market, account age), every submitted field, every document (rendered inline where the format allows or downloadable), the full state-transition history, every prior decision reason, and the schema version that was applied at submission time.
- **FR-014**: Reviewers MUST enter a reason on every decision (`approve`, `reject`, `info-request`, `revoke`); the system MUST reject decisions submitted without a non-empty reason.
- **FR-015**: A `revoke` action MUST require the dedicated `verification.revoke` permission, distinct from the standard `verification.review` permission.
- **FR-015a**: PII access scope MUST be enforced as follows: (a) **uploaded documents** — readable only by holders of `verification.review` (active state) or via the audited "open historical document" action (terminal state); (b) **license / regulator-identifier values** — readable by holders of `verification.review` and by holders of a dedicated `verification.read_pii` permission granted to the customer-account admin role defined in spec 019; (c) **super-admin** — sees everything as part of the role's broad scope; (d) **support agents (spec 023)** — see only the verification **state** and a **decision reason summary** localized to the customer's locale, and MUST NOT see raw license-number values or documents; (e) every read of documents or of raw license-number values under (a) or (b) MUST be recorded in the audit log with actor, timestamp, verification reference, and (for documents) document reference.
- **FR-016**: Two-reviewer concurrency MUST be guarded: only one decision wins; the loser sees a clear, localized "request already decided" error.
- **FR-016a**: V1 MUST NOT call any external regulator register (e.g. SCFHS, EG dental syndicate) as part of the verification flow. Decisions are made manually by reviewers based on customer-supplied fields and uploaded documents only.
- **FR-016b**: The reviewer detail view layout and the verification entity MUST reserve an extension point for a future "regulator-assist panel" (planned for Phase 1.5+). Adding the panel later MUST NOT require changes to the verification state machine, the eligibility query contract, or the customer submission flow.

#### Expiry and renewal

- **FR-017**: Approvals MUST carry an expiry date set at approval time according to per-market policy (default: 365 days, configurable).
- **FR-018**: A scheduled job MUST move expired approvals to `expired` state and write an audit entry attributing the system as actor and `expiry_reached` as reason.
- **FR-019**: Reminder notifications MUST be triggered on each configured reminder window before expiry (default windows: 30 / 14 / 7 / 1 days), via spec 025, in the customer's locale, on their preferred channel; duplicate reminders for the same window MUST NOT be sent.
- **FR-020**: When a renewal is approved while a prior approval is still active, the prior approval MUST transition to `superseded`, the audit log MUST link the two verifications, and the new approval's expiry MUST replace the old one.

#### Restricted-product eligibility hook (consumed by 005 / 009 / 010)

- **FR-021**: The system MUST expose a single canonical eligibility query of the form `is_eligible(customer, sku) → { eligible | not_eligible, reason_code, localized_message_key }` that already accounts for verification state, expiry, market, profession, and product restriction policy.
- **FR-022**: The eligibility query MUST return a stable, machine-readable `reason_code` from a documented enum (e.g. `eligible`, `verification_required`, `verification_pending`, `verification_info_requested`, `verification_rejected`, `verification_expired`, `verification_revoked`, `profession_mismatch`, `market_mismatch`, `account_inactive`, `unrestricted`).
- **FR-023**: The eligibility query MUST be deterministic for a given customer + sku + point-in-time; the same inputs MUST yield the same answer until verification state changes.
- **FR-024**: The eligibility hook MUST be the only authoritative source of "may this customer purchase this restricted SKU?" — catalog (005), cart (009), and checkout (010) MUST NOT reimplement the policy.

#### Market awareness (Principle 5)

- **FR-025**: Required submission fields and document types MUST be driven by market configuration, not hardcoded; market configuration changes MUST take effect for new submissions without a code deploy.
- **FR-026**: Every verification MUST capture the market it was submitted under; reviewers MUST see the schema and labels that were applied at submission time, not the current configuration.
- **FR-027**: A change of the customer's market-of-record MUST: (a) void any in-flight non-terminal verification (`submitted`, `in-review`, `info-requested`) with reason `customer_market_changed`, and (b) close any active approval as `superseded`. A new submission for the new market is required; cross-market eligibility MUST NOT be conferred. The voided rows' `schema_version` + `restriction_policy_snapshot` are preserved for audit-replay even though the customer cannot resubmit against them.

#### Audit (Principle 25)

- **FR-028**: Every state transition (customer-driven, reviewer-driven, system-driven) MUST write an audit entry capturing: actor identity (or `system`), timestamp, prior state, new state, reason text, and any structured metadata (document references, supersedes-link, market).
- **FR-029**: Audit entries MUST be immutable and reuse the platform-wide `audit_log_entries` infrastructure established in spec 003.
- **FR-030**: The submission detail view MUST be able to render the audit history for a verification end-to-end without separate queries to other modules.

#### Bilingual + RTL (Principle 4)

- **FR-031**: All customer-facing screens, validation messages, decision reasons surfaced to customers, push notifications, emails, SMS, and PDFs produced by this spec MUST be available in Arabic and English; Arabic content MUST be editorial-grade.
- **FR-032**: All customer-facing surfaces MUST mirror to RTL when the locale is Arabic, including form inputs, status badges, document previews, timeline, and error states.
- **FR-033**: Reviewer-facing surfaces (admin) MUST be available in Arabic and English; reviewer reason text is captured as free text and is NOT auto-translated to the customer's locale. To enforce this:
  - Every decision request body (`approve`, `reject`, `request-info`, `revoke`) MUST accept a structured reason object `{ "en"?: string, "ar"?: string }` where at least one locale is required; submissions providing neither are rejected with `verification.reason_required`.
  - The reviewer's submission detail view MUST display, prominently, the customer's preferred locale (resolved from spec 004 identity context).
  - When the reviewer submits a reason in only one locale and the customer's preferred locale is the other one, the system MUST accept the submission and display the available reason to the customer with a one-line localized notice "(reviewer left this in {OtherLocale})".
  - When both locales are provided, the customer-facing renderings (push, email, SMS, in-app) MUST use the customer's preferred locale; the audit log preserves both.

#### Notifications integration (Principle 19)

- **FR-034**: Decisions (`approved`, `rejected`, `info-requested`, `revoked`, `expired`) MUST trigger notifications via spec 025 on the customer's preferred channel(s) in their locale; verification state writes MUST NOT depend on notification success.
- **FR-035**: Renewal reminders MUST be triggered via spec 025 per FR-019.

#### Multi-vendor readiness (Principle 6)

- **FR-036**: The verification entity, reviewer assignment, queue scoping, and audit model MUST be designed so that a future `vendor_id` dimension can be added to reviewer scope and to product-restriction policy without altering the verification state machine, the eligibility query contract, or the customer submission flow.

#### Operational safeguards

- **FR-037**: The customer MUST be able to see when they may resubmit after a rejection (the cool-down clock) and the platform MUST enforce it server-side.
- **FR-038**: When the underlying customer account is locked or deleted by spec 004 actions, in-flight verifications MUST move to `void` state and approved verifications MUST be treated as ineligible by the eligibility hook.
- **FR-039**: The system MUST surface queue age signals (oldest-pending age per market) so admins can detect SLA breach without leaving the verification module; the system MUST NOT auto-decide aged requests. The V1 SLA target is a decision (approve / reject / info-request) within **2 business days** of submission, computed against the market's local working calendar (Sun–Thu for both KSA and EG). The verification module MUST render two distinct signals: **warning** when a non-terminal verification is older than **1 business day** without a decision, and **breach** when older than **2 business days**. SLA timers MUST pause while the verification is in `info-requested` state (waiting on the customer) and MUST resume once the customer resubmits.

### Key Entities

- **Verification**: A single submission-and-decision record for one customer at one point in time. Carries: customer reference, market-of-record at submission, profession, regulator identifier, schema version, current state, expiry date (if approved), supersedes-link (if a renewal), revocation reference (if revoked), submitted-at, decided-at, decided-by.
- **VerificationDocument**: A storage-abstracted reference to one uploaded supporting document for a Verification. Carries: storage key, content type, scan status, uploaded-at, and a derived `purge_after` timestamp once the parent Verification reaches a terminal state (computed from the per-market retention window: KSA 24mo / EG 36mo by default). Documents are owned by the Verification and are not shared across verifications.
- **VerificationStateTransition**: An immutable record of one transition for one Verification. Carries: actor (customer / reviewer-id / `system`), timestamp, prior state, new state, reason text, structured metadata. Renders the timeline in both customer and reviewer surfaces; also written to the platform `audit_log_entries`.
- **VerificationMarketSchema**: The market-scoped definition of which fields and document types are required for a submission, and the cool-down + expiry + reminder-window policy. Versioned so a Verification can be reviewed against the schema in effect when it was submitted.
- **EligibilityQuery (read-model)**: The canonical answer to "for customer X and SKU Y, is purchase eligible right now?" — derived from Verification state plus product restriction policy plus market. Not a persisted entity in the editable sense; it is a cached, deterministic projection that is invalidated on any verification state change for the customer.
- **VerificationReminder**: A record that a reminder was emitted for a specific Verification at a specific reminder-window boundary. Used to enforce the no-duplicate-reminder rule.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new customer can complete a verification submission (open form → submit profession + identifier + one document → confirmation screen) in under 3 minutes on a typical mobile connection in their preferred locale, end-to-end.
- **SC-002**: A reviewer can open a queued submission, review the documents, enter a decision and reason, and confirm in under 90 seconds for a straightforward case; the queue detail view loads in under 2 seconds for a submission with up to 5 documents.
- **SC-003**: 100% of decisions (approve / reject / info-request / revoke / system-expiry) write a corresponding immutable audit entry with actor, timestamp, prior state, new state, and reason — verifiable by replaying any verification's full history from audit logs alone.
- **SC-004**: The eligibility query returns an answer fast enough that no surface (catalog list, product detail, cart, checkout) shows a measurable degradation when the query is added — practically: 95% of calls return in under the budget that 005 / 009 / 010 set for a synchronous in-process call (acceptance budget locked in `/plan`).
- **SC-005**: For every approved verification, a renewal-reminder notification is delivered (or queued for retry per spec 025) at every configured reminder window (30 / 14 / 7 / 1 days by default) with zero duplicates per window across at least one full simulated expiry cycle in staging.
- **SC-006**: 100% of customer-facing strings, decision-reason rendering, validation errors, push / email / SMS templates, and PDFs produced by this spec pass the Arabic editorial review (Principle 4) and the RTL visual sweep (no LTR-mirrored screens, no clipped or overflowing labels).
- **SC-007**: Across 100 simulated decisions on a single submission attempted by two reviewers concurrently in a load test, exactly one decision commits per submission — no double-decision, no stale-state writes, no audit-log gaps.
- **SC-008**: Restricted-product eligibility on storefront (005), cart (009), and checkout (010) agree 100% of the time across a synthetic matrix of (customer-state × product-restriction × market), proving FR-021 / FR-024 are enforced as the single source of truth.
- **SC-009**: Verifications expire automatically within one scheduled job interval after their expiry timestamp passes; eligibility for those customers flips to `not_eligible: verification_expired` no later than the next eligibility query for any of those customers, and never sooner than the configured expiry timestamp.
- **SC-010**: A market-config update that adds a new required field for KSA takes effect for new KSA submissions without a code deploy, and existing in-flight submissions submitted under the prior schema continue to render against the schema they were submitted under (FR-026).
- **SC-011**: 95% of `submitted` verifications receive their first decision (approve / reject / info-request) within 2 business days of submission, measured per market against the market's local working calendar; the verification module surfaces a `warning` signal at 1 business day and a `breach` signal at 2 business days, both observable without leaving the module.

---

## Assumptions

- Customer authentication, role / permission infrastructure, and audit-log primitives are provided by spec 004 (`identity-and-access`) and spec 003 (audit infrastructure) and are at DoD on `main` before this spec's implementation begins.
- The admin web shell and admin-side RBAC surfaces are provided by spec 015 (`admin-foundation`); 015's contract is merged to `main` before reviewer UI work in this spec begins, but the eligibility-hook backend can land before 015 ships.
- The notification module (spec 025) is **not** a hard build-time dependency for the eligibility loop. This spec defines and emits the events 025 will subscribe to; if 025 is not yet deployed, decisions still commit and audit but no outbound notification is sent. Renewal reminders (User Story 4 / FR-019) are dependent on 025 being live to be observable by the customer; the schedule + event emission ship in this spec regardless.
- A storage abstraction supporting size limits, content-type allow-list, and antivirus / malware scanning is available platform-wide. This spec consumes it; it does not define a new one.
- The default market-configuration policy (cool-down 7 days, expiry 365 days, reminder windows 30 / 14 / 7 / 1 days) is editable per market through the admin market-config surface defined in spec 003 / 015; concrete EG and KSA regulator field-set definitions are owned by the operations team and provided as configuration, not as code in this spec.
- Customer-facing surfaces are the Flutter mobile app and the Flutter web storefront delivered by Phase 1C UI specs; this spec's customer flow is consumed by them through the contracts published here.
- The eligibility query is consumed in-process by 005 / 009 / 010 within the modular monolith (ADR-003); future service extraction is allowed but not in scope for this spec.
- Phase 2 (multi-vendor) is out of scope; the verification model is designed to accept a vendor dimension later (FR-036) but this spec ships single-vendor.
- B2B company accounts (spec 021) are out of scope here; if a company-account model is later required to verify on behalf of multiple users, that is an extension to this spec, not a redesign.
- External regulator-register integration (SCFHS, EG dental syndicate, etc.) is **out of scope for V1**; the data model and reviewer UI reserve an extension point so an assistive-lookup or auto-decisioning capability can be added in Phase 1.5+ without contract change.
