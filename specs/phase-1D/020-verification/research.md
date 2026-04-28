# Phase 0 — Research: Professional Verification (Spec 020)

**Date**: 2026-04-28
**Spec**: [spec.md](./spec.md)
**Plan**: [plan.md](./plan.md)

This document closes every NEEDS-CLARIFICATION-shaped open question that the plan's Technical Context or Constitution gates left implicit. The five spec-level clarifications recorded in `spec.md` §Clarifications (Session 2026-04-28) are not repeated; the items below are the implementation-research questions that remained after those.

---

## R1. Eligibility query — read-model shape, latency budget, invalidation strategy

**Decision**: Maintain a `verification_eligibility_cache` projection table in the same Postgres database, updated synchronously inside every state transition's transaction. Schema: `(customer_id PK, market_code, eligibility_class, expires_at nullable, computed_at)`. Eligibility queries join this projection with `IProductRestrictionPolicy.GetForSku(sku)` and resolve the answer in-process.

**Latency budget (locks SC-004 placeholder)**: p95 ≤ 5 ms, p99 ≤ 15 ms, measured at the `IRequestHandler<EligibilityQuery>` boundary on the catalog-list hot path. Catalog list pages may call once per restricted SKU per page; a 50-item page with 10 restricted SKUs stays under 50 ms aggregate.

**Rationale**:
- A real Postgres materialized view cannot be `REFRESH`ed inside the same Tx as the state-changing INSERT/UPDATE; cross-Tx visibility windows would let a just-approved customer see "ineligible" on their immediate next page-load.
- A separate read-projection table written transactionally with the state transition is the minimum infrastructure needed to satisfy FR-021 + SC-008's "100% agreement across catalog/cart/checkout".
- The bloom-filter caching pattern from spec 004 is overkill: eligibility cardinality is bounded by `(customer × market)`, easily indexed, and the query plan is a single-row PK lookup plus a small policy JOIN.
- In-process call (modular monolith, ADR-023) avoids cross-service latency. Future service extraction is allowed (spec 020 doesn't preclude it) but not required.

**Alternatives considered**:
- **Recompute from base tables on every call**: forces N+1 pattern for catalog list (50-item page × 1 verification SELECT per item); risks p99 blowup at scale; non-deterministic during high-write windows.
- **Materialized view with periodic REFRESH**: cross-Tx visibility window violates Story 1 Acceptance Scenario 4.
- **Redis cache**: introduces a second persistence dependency for a single read pattern; cache invalidation becomes its own correctness problem; spec 020 should not be the first module to add Redis to the stack.
- **Push-based eligibility events with consumer-side cache**: catalog/cart/checkout would each need to maintain their own read model — three sources of bug rather than one.

**Invalidation triggers** (every one happens inside the state transition's Tx):
1. `submitted | in-review | info-requested → approved` — INSERT-or-UPDATE cache row to `(class=Eligible, expires_at=approved_until)`.
2. `approved → expired | revoked | superseded | void` — UPSERT cache row to `(class=Ineligible-with-reason, expires_at=null)`.
3. Customer market change (FR-027) — wholesale UPSERT to `(class=Ineligible:market_mismatch)`.
4. Account lifecycle "locked / deleted" (FR-038, R7) — UPSERT to `(class=Ineligible:account_inactive)`.

A scheduled "cache reconciliation" job (sweeps cache rows whose `computed_at` is older than 24 h and rewrites them from authoritative state) is **out of scope for V1**; correctness depends on every transition being inside the Tx, which is asserted by integration tests.

---

## R2. Business-day calculator for SLA timers and customer cool-down

**Decision**: Implement `BusinessDayCalculator` in `Modules/Verification/Primitives/`. Default working week: Sunday–Thursday for both KSA and EG markets. Holidays are configurable per market via a `verification_market_schemas.holidays_list` jsonb column (empty list in V1; Operations adds entries before launch). Pure function (no time injection here — `TimeProvider` is injected at call site).

**Rationale**:
- KSA standard private-sector working week is Sun–Thu; EG public sector matches. Friday + Saturday are weekend in both markets.
- A configurable holiday list per market is the minimum needed to avoid breach-signal noise on national holidays (KSA National Day, EG Revolution Day) without shipping a full holidays-as-a-service.
- Pure function is straightforward to test and reuse (Story 4 reminder windows could in future also count business days, though FR-019 currently uses calendar days for 30/14/7/1 — noted; not changing).

**Alternatives considered**:
- **`Nager.Date` library**: brings a country-holiday catalog; adds a dependency for one column's worth of behavior; library lists do not match KSA / EG operations team's preferred holidays definition.
- **Hardcoded weekend = Sat–Sun**: wrong for both markets; would silently breach SLA on every Sun.

**SLA semantics implemented**:
- Submitted-at = T0.
- Warning at: `BusinessDayCalculator.AddBusinessDays(T0, market.sla_warning_business_days)` (default 1).
- Breach at: `BusinessDayCalculator.AddBusinessDays(T0, market.sla_decision_business_days)` (default 2).
- Pause: while in `info-requested`, the SLA clock is paused — the projected warning/breach instants are recomputed when the customer resubmits, with elapsed in-customer-hands time excluded.

---

## R3. Document storage, allowed types, AV scan, and purge

**Decision**: Reuse existing `Modules/Storage/IStorageService` + `IVirusScanService`. Allowed content types (V1, platform ceiling): `application/pdf`, `image/jpeg`, `image/png`, `image/heic`. Per-document max 10 MB; per-submission max 5 documents; per-submission aggregate max 25 MB (FR-006).

**Purge model**: `VerificationDocument.PurgeAfter` is computed at the moment the parent `Verification` enters any terminal state, as `terminal_at + market.retention_months`. `VerificationDocumentPurgeWorker` runs daily, finds all documents with `PurgeAfter <= now`, calls `IStorageService.Delete(storageKey)`, then sets the row's `PurgedAt` and `StorageKey = null`. The `VerificationDocument` row is **not** deleted (preserves entity + audit linkage); only the blob and the storage key are removed.

**Rationale**:
- Reusing existing storage seam matches the project's modular-monolith philosophy and avoids a second blob lifecycle.
- Allowed types cover real-world license documents (PDF + scan/photo formats) without expanding the AV-scan attack surface (no Office, no archives).
- Daily worker pattern matches Spec 008's reorder-alert pattern; idempotent sweep is fault-tolerant against worker outages.
- Preserving the row keeps `VerificationStateTransition` history navigable: a transition that references a now-purged document still has a row to point at; the UI shows "document purged on YYYY-MM-DD per retention policy".

**Alternatives considered**:
- **Hard-delete the row**: breaks audit-trail navigation; reviewers opening a historical record see broken links instead of an explicit retention-purge marker.
- **Per-document scheduler (Hangfire-like)**: introduces a job-scheduling subsystem just for this one timer; not justified at V1 scale.
- **Cron job (cron-style outside the app)**: works but loses the type-safe `IStorageService` boundary; harder to test with `FakeTimeProvider`.

---

## R4. Concurrency guard for two-reviewer simultaneous decisions (FR-016)

**Decision**: Use EF Core optimistic concurrency via Postgres `xmin` system column mapped as `IsRowVersion()`. Every decision command loads the current `Verification`, mutates it, and `SaveChangesAsync()`. The loser receives `DbUpdateConcurrencyException`, which the handler maps to a `409 Conflict` with reason code `verification.already_decided`.

**Rationale**:
- `xmin` is a zero-storage, zero-trigger optimistic-concurrency token native to Postgres; spec 008 already uses this pattern for stock writes (per project memory).
- Story 2 Scenario 7 explicitly asserts "exactly one decision wins; no double-write to the audit log". Mapping `DbUpdateConcurrencyException` to `409` + structured reason code keeps the contract testable.
- Idempotency (different concern, FR-001/FR-016) is handled separately at the request middleware layer using the platform `Idempotency-Key` header per spec 003. The two layers compose: idempotency replays the original 200; concurrency rejects a competing decision.

**Alternatives considered**:
- **Pessimistic row lock (`SELECT ... FOR UPDATE`)**: serializes reviewer activity inside a Tx; works but reduces observable parallelism in the queue UI; harder to test deterministically.
- **Application-layer mutex / Redis lock**: introduces a dependency for one race condition.
- **Advisory locks**: Postgres-specific surface area used by no other module; not worth the complexity.

---

## R5. Reminder de-duplication semantics (FR-019)

**Decision**: Maintain `verification_reminders` table with unique index `(verification_id, window_days)`. The reminder worker iterates approved verifications whose expiry is within an unfired reminder window, attempts an INSERT into `verification_reminders`, and on success publishes `VerificationReminderDue { verificationId, windowDays }`. INSERT failure (unique-constraint violation) means another worker already fired this window — the publish is skipped.

**Back-window skip**: when the worker resumes after a multi-window outage and finds two unfired windows for the same verification (e.g., 14 and 7 days both expired during the outage), only the **closest unfired window** is fired (the smallest `window_days` ≤ `(expires_at - now)`). The other windows are recorded as `verification_reminders` rows with `skipped=true` and an audit-log note explaining the skip. Customer is not bombarded.

**Rationale**:
- FR-019's "duplicate reminders for the same window MUST NOT be sent" is a uniqueness invariant, best expressed by a unique index, not by mutable last-fired state on the parent row.
- The closest-unfired-window rule matches Story 4's edge-case "do not flood the customer".
- The skip audit note keeps the operator able to explain "why didn't customer X get the 14-day reminder?" — answer: outage, here's the audit row.

**Alternatives considered**:
- **`Verification.LastReminderWindowDays` field**: race-prone; back-window skip becomes a comparison instead of a record; lost the audit story.
- **Notification-side dedup (push key into spec 025)**: leaks verification semantics into the notification module; spec 025's job is delivery, not domain-event dedup.

---

## R6. Customer market change effect on active verifications (FR-027)

**Decision**: Subscribe to a new `ICustomerAccountLifecycleSubscriber` (declared in `Modules/Shared/`) for the event `CustomerMarketChanged { customerId, fromMarket, toMarket, changedBy, occurredAt }`. On receipt, the verification module:
1. Voids any non-terminal verification owned by that customer (state → `void`, actor `system`, reason `customer_market_changed`).
2. Supersedes any active `approved` verification (state → `superseded`, actor `system`, reason `customer_market_changed`).
3. UPSERT cache row to `(class=Ineligible:market_mismatch)`.

Spec 004 publishes the event; this spec defines the contract in `Modules/Shared/`.

**Rationale**:
- Cross-module event flows go through `Modules/Shared/` to avoid cycles (project memory: cross-module hooks via Modules/Shared/).
- "Cross-market verification MUST NOT be conferred" (FR-027) is non-negotiable; the only way to honor it deterministically is to terminate the prior approval and require a new submission for the new market.
- The customer is notified through spec 025 via the standard `VerificationSuperseded` / `VerificationVoided` events.

**Alternatives considered**:
- **Lazily check market mismatch in the eligibility query**: defers the work; cache rows go stale and "the answer is consistent until verification state changes" (FR-023) is harder to reason about.
- **Allow active approval to cross markets**: violates FR-027.

---

## R7. Account lifecycle (locked / deleted) effect on verifications (FR-038)

**Decision**: Same `ICustomerAccountLifecycleSubscriber` mechanism as R6. Events: `CustomerAccountLocked`, `CustomerAccountDeleted`. On receipt:
- Non-terminal verifications transition to `void` with reason `account_inactive` (locked) or `account_deleted` (deleted).
- Active `approved` verifications transition to `void`.
- Eligibility cache UPSERT to `(class=Ineligible:account_inactive)` or `(class=Ineligible:account_deleted)`.
- For `account_deleted`, a follow-up "PII expedited purge" trigger marks documents `PurgeAfter = now` so the next worker run removes them ahead of the normal retention window. (Right-to-erasure conformance; documented as a configurable option in `VerificationMarketSchema` for jurisdictions where retention vs erasure obligations conflict.)

**Rationale**:
- A locked account must not retain restricted-purchase eligibility — security posture.
- A deleted account triggers the strongest data-minimization stance (PDPL right-to-erasure); documents are purged sooner than the retention window otherwise dictates.
- One subscriber, three events: keeps the integration surface small.

**Alternatives considered**:
- **Periodic reconciliation of account-state vs verification-state**: introduces a 24 h vulnerability window where a locked account could still purchase; unacceptable.

---

## R8. Reason codes — machine-readable enum + ICU keys

**Decision**: `EligibilityReasonCode` (enum) and `VerificationReasonCode` (enum) live in `Modules/Verification/Primitives/`. Each enum value maps to:
1. An ICU key in `verification.en.icu` and `verification.ar.icu` (e.g. `verification.eligibility.expired` → "Your verification expired on {date}." / "انتهت صلاحية توثيقك في {date}.").
2. A documented entry in `contracts/verification-contract.md` with the structural shape (which placeholders are filled, which not).

`EligibilityReasonCode` enum (V1):
`Eligible`, `Unrestricted`, `VerificationRequired`, `VerificationPending`, `VerificationInfoRequested`, `VerificationRejected`, `VerificationExpired`, `VerificationRevoked`, `ProfessionMismatch`, `MarketMismatch`, `AccountInactive`.

**Rationale**:
- Catalog (005), cart (009), and checkout (010) consume reason codes to render localized messaging and to choose CTAs ("Verify now", "Renew now", "Contact support"). Stable codes are the contract.
- Splitting machine-readable enum from human-readable ICU keys lets the contract be the enum (versioned with the API) and the strings be editorial output (versioned with the locale bundle).

**Alternatives considered**:
- **Boolean `eligible: bool` only**: forces every consumer to reimplement messaging; violates FR-024 single-source-of-truth.
- **String reason codes**: typo-prone; not compile-time-checked at consumer sites.

---

## R9. Authorization wiring across modules

**Decision**: This spec declares the permission constants:
- `verification.review` — read queue + read detail + decide approve / reject / request-info + open historical document.
- `verification.revoke` — strictly the revoke action.
- `verification.read_pii` — read raw `LicenseNumber` outside the reviewer queue (e.g., from spec 019's customer-account admin surface).

Spec 015 (admin-foundation) defines roles. Spec 019 (admin-customers) is the role that gets `verification.read_pii` in its role→permission seed. Super-admin gets all three implicitly per spec 004's role model. Support agents (spec 023) are explicitly **not** granted any of these; they see verification state + reason summary via a separate `verification.read_summary` permission that the admin-support role (spec 023) holds.

**Rationale**:
- Permissions are owned by the enforcing module (here); roles are owned by the composing module (015 / 019 / 023). This matches spec 004's pattern.
- Adding `verification.read_summary` lets the support-agent surface render verification state + reason without ever touching license-number or document blobs — the PDPL minimization posture from spec.md §Clarifications Q5.

**Alternatives considered**:
- **Single `verification.read` permission**: collapses three different access scopes into one; spec 023 support agents would either over-see (PII) or not-see (forced escalation for every state check).

---

## R10. Renewal mechanics (FR-010, FR-020)

**Decision**: A renewal is a new `Verification` row whose `SupersedesId` points at the prior approved row. While the renewal is in any non-terminal state, the prior approval **stays active** — eligibility queries still return `Eligible` until the renewal commits a decision. On `approved`, the prior row is transitioned to `superseded` with reason `renewed_by={newId}` and the eligibility cache row is rewritten to the new approval's `expires_at`. On `rejected`, the prior approval is untouched (FR-020).

**Renewal eligibility window**: the `RequestRenewal` endpoint is callable only when (a) the customer has an active approved verification, and (b) `now >= expires_at - max(market.reminder_windows_days)` — i.e., once the earliest reminder window has opened. Outside this window the endpoint returns `409 verification.renewal_window_not_open`.

**Rationale**:
- Modeling renewal as "a new submission with a back-pointer" matches spec.md key entity description and avoids forking the entity model.
- Bounding the window prevents customers from holding a perpetual two-row queue position; reviewers see a clear "renewal of #123" badge in the queue UI.

**Alternatives considered**:
- **Edit-in-place renewal** (mutate the existing approved row): destroys the original audit story for the prior approval; violates Principle 25.
- **Always allow renewal at any time**: floods the queue with premature renewals; reviewers spend cycles on submissions whose original is still 6 months from expiry.

---

## R11. External regulator integration extension point (FR-016b)

**Decision**: Reserve an `IRegulatorAssistLookup` interface in `Modules/Shared/` with a single method `Task<RegulatorAssistResult?> LookupAsync(MarketCode market, string regulatorIdentifier, CancellationToken ct)`. **No implementation** in V1; DI registration is a `NullRegulatorAssistLookup` that returns `null`. The reviewer detail handler conditionally calls it and renders a panel if the result is non-null. Adding a real implementation in Phase 1.5+ requires only swapping the DI registration and writing the adapter; the verification state machine, the eligibility query contract, and the customer flow are untouched.

**Rationale**:
- FR-016b explicitly mandates a future-proofing extension point without contract change.
- Returning `null` from the V1 default keeps the reviewer panel hidden by default; no UI flag to flip later.

**Alternatives considered**:
- **No interface at all**: forces a Phase 1.5 redesign of the reviewer detail handler; fails FR-016b literally.
- **Plugin model with discovery**: overkill for one future provider.

---

## R12. Worker scheduling — daily cadence and ordering

**Decision**: Three `IHostedService` workers, all using `BackgroundService` with a `PeriodicTimer` set to 24 h. Default UTC start times (configurable via `appsettings.json`, locked into Key Vault for Staging/Prod):
- `VerificationExpiryWorker` — 03:00 UTC. Runs first because expiry transitions invalidate eligibility cache and emit `VerificationExpired` events.
- `VerificationReminderWorker` — 03:30 UTC. Runs after expiry so it does not re-emit reminders for verifications that just expired.
- `VerificationDocumentPurgeWorker` — 04:00 UTC. Runs last so any expiry / supersede transitions earlier in the night have already set `PurgeAfter` correctly.

Each worker takes a Postgres advisory lock (`pg_try_advisory_lock(key)`) before running so that horizontal-scaled instances don't double-execute. Workers are `FakeTimeProvider`-friendly via injected `TimeProvider`.

**Rationale**:
- Daily cadence matches FR-018 / FR-019 / FR-006a "after the window elapses" wording. Sub-hourly is unnecessary overhead.
- 03:00 UTC = 06:00 KSA / 05:00 EG — well outside customer peak hours in both markets; reduces impact of the reminder push/email burst.
- Advisory locks are the lightest horizontal-scale guard in the .NET-on-Postgres stack and require no additional infrastructure.

**Alternatives considered**:
- **Hourly cadence**: 24× more wakeups for a daily-meaningful effect; not justified.
- **Quartz.NET / Hangfire**: full job scheduler for three jobs; 020 should not be the module that introduces it.
- **Lease-based coordination via Redis**: introduces Redis dependency for a non-time-critical coordination problem.

---

## R13. PII-access auditing surface (FR-015a-e, FR-006b)

**Decision**: Introduce `IPiiAccessRecorder` in `Modules/Verification/Primitives/` with a single method `RecordAsync(PiiAccessKind kind, Guid verificationId, Guid? documentId, CancellationToken ct)`. `kind ∈ { LicenseNumberRead, DocumentBodyRead, DocumentMetadataRead }`. Implementation writes a row to `audit_log_entries` via the existing `IAuditEventPublisher` with a stable `event_kind = "verification.pii_access"` and a structured payload.

**Rationale**:
- Centralizes PII-read recording at one chokepoint so a future auditor can grep `event_kind` to find every read.
- Reuses spec 003's audit infrastructure — no new audit surface invented for one module.

**Alternatives considered**:
- **Inline `IAuditEventPublisher.Publish(...)` calls at each read site**: works but invites future drift (a new read site forgets to call). One-method abstraction is cheap insurance.
- **EF Core query interceptor**: too magic for a security-critical concern; explicit beats implicit.

---

## R14. EF Core warning suppression and DI scope (project-memory rule)

**Decision**: `VerificationModule.cs`'s `AddDbContext<VerificationDbContext>` registration suppresses `RelationalEventId.ManyServiceProvidersCreatedWarning` (per the project-memory rule "every new module's AddDbContext must suppress or Identity tests break"). Scope is `Scoped` (default).

**Rationale**: Documented project-wide pattern. Skipping it breaks Identity test isolation.

---

## R15. OpenAPI artifact convention

**Decision**: Generate `services/backend_api/openapi.verification.json` at the same root as the existing per-module artifacts (`openapi.identity.json`, `openapi.catalog.json`, etc.). Generated by the existing OpenAPI emitter pipeline. Reviewers diff this file in PRs to satisfy Guardrail #2 (contract diff).

---

## Open items deferred (with justification)

| Item | Why deferred | Where it lands |
|---|---|---|
| Real regulator-register API integration (SCFHS, EG syndicate) | Spec.md §Clarifications Q2: out of scope for V1 (Option A). Extension point is the V1 commitment. | Phase 1.5+ extension PR; no spec 020 change required. |
| Column-level encryption of `LicenseNumber` | Azure Postgres TDE covers at-rest; PDPL/EG-PDP audit will determine if column-level encryption is required. | Spec 028's audit scope. |
| Customer-facing notification preference UI for verification reminders | Owned by spec 1.5-e (notifications-preference-ui). Spec 025 ships preference basics in 1D. | Phase 1.5-e. |
| Multi-vendor reviewer scoping | FR-036 reserves the slot; populating `vendor_id` on reviewer-queue filters waits for Phase 2 marketplace. | Phase 2-b (vendor-catalog-ownership). |
| WhatsApp channel for reminders | Spec 1.5-f. | Phase 1.5-f. |
