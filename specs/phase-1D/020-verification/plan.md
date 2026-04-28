# Implementation Plan: Professional Verification

**Branch**: `phase_1D_creating_specs` (working) · target merge: `020-verification` | **Date**: 2026-04-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1D/020-verification/spec.md`

## Summary

Deliver the verification subsystem that turns the constitutional Principle-8 restricted-product policy into a deterministic, auditable, market-aware capability:

1. **One state machine** (Principle 24): `Verification` over `submitted → in-review → (approved | rejected | info-requested) → (info-requested ↔ in-review)`, plus terminal/derived states `expired`, `revoked`, `superseded`, `void`. Every transition emits an `AuditEvent` via spec 003's `IAuditEventPublisher` (Principle 25).
2. **Customer surface** (HTTP slices) for submission, status, document upload, info-requested resubmission, renewal — fully bilingual AR + EN with editorial-grade copy and RTL parity (Principle 4).
3. **Admin reviewer surface** (HTTP slices) for queue, detail, decision actions (`approve`, `reject`, `request-info`, `revoke`), and audited "open historical document" — RBAC-gated by three new permissions: `verification.review`, `verification.revoke`, `verification.read_pii` (FR-015 / FR-015a; spec 019 binds the latter to the customer-account admin role).
4. **Eligibility query hook** (`ICustomerVerificationEligibilityQuery` in `Modules/Shared/`) — the **single authoritative source** for "may this customer purchase this restricted SKU right now?". Catalog (005), Cart (009), and Checkout (010) consume it in-process; they MUST NOT reimplement the policy (FR-021–FR-024). Latency budget: p95 ≤ 5 ms (locks SC-004 placeholder).
5. **Three background workers** (HostedService): `VerificationExpiryWorker` (daily — moves expired approvals to `expired`); `VerificationReminderWorker` (daily — emits 30/14/7/1-day renewal-reminder events); `VerificationDocumentPurgeWorker` (daily — purges documents past their `purge_after` while preserving entity + audit trail per FR-006a / FR-006b).
6. **Market-aware schema** (Principle 5): `VerificationMarketSchema` rows define required fields, document types, retention window (KSA 24mo / EG 36mo defaults), cool-down (default 7d post-rejection), expiry (default 365d), reminder windows (30/14/7/1 days), and SLA target (2 business days, warning at 1 business day, breach at 2). Schema is **versioned** so reviewers see what the customer saw at submission time (FR-026). No external regulator API in V1 (FR-016a / FR-016b); a typed extension point reserves space for an assistive-lookup panel later.
7. **Notifications integration** (Principle 19): every customer-affecting transition (`approved`, `rejected`, `info-requested`, `revoked`, `expired`) and every reminder window publish a `VerificationDomainEvent` to which spec 025 subscribes. Verification state writes never depend on notification success (FR-034).
8. **Multi-vendor readiness** (Principle 6): `Verification.RestrictionPolicyAt` capture, reviewer-scope queue filters, and the eligibility-query interface all reserve a future `VendorId` dimension without altering the state machine, the eligibility contract, or the customer flow (FR-036).

No UI ships in this spec (admin web UI is owned by spec 015 + 016/019 wiring). Lane A merges contracts; Lane B builds against them per the plan's per-feature handoff rule.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (LTS), PostgreSQL 16 (per spec 004 + ADR-022).

**Primary Dependencies**:
- `MediatR` v12.x + `FluentValidation` v11.x — vertical-slice handlers (ADR-003).
- `Microsoft.EntityFrameworkCore` v9.x — code-first migrations (ADR-004).
- `Microsoft.AspNetCore.Authorization` (built-in) — `[RequirePermission("verification.*")]` attributes from spec 004's RBAC.
- `Modules/Storage/IStorageService` (existing) — document upload, signed retrieval, deletion (FR-006).
- `Modules/Storage/IVirusScanService` (existing) — content-type allow-list + AV scan (FR-006).
- `Modules/AuditLog/IAuditEventPublisher` (existing) — every transition + every PII read (FR-028 / FR-015a-e).
- `Modules/Identity` consumables — `ICustomerPostSignInHook` pattern + new `ICustomerAccountLifecycleSubscriber` for void-on-account-deactivate (Edge Case "underlying account is locked or deleted"; FR-038).
- `MessageFormat.NET` (already vendored by spec 003) — ICU AR/EN keys for every customer-facing reason code.
- Spec 005 (`catalog`) consumable: `IProductRestrictionPolicy` (read-only): given a SKU, returns `{ restricted_in_markets, required_profession, required_eligibility_class, vendor_id (nullable for V1) }`. **Contract owned by spec 005**, but this spec's eligibility query depends on it; the seam is declared in `Modules/Shared/IProductRestrictionPolicy.cs` so 005 implements and 020/009/010 consume.

**Storage**: PostgreSQL (Azure Saudi Arabia Central per ADR-010). 5 new tables in the `verification` schema:
- `verifications` — one row per verification (state, customer, market, profession, regulator identifier, submitted-at, decided-at, decided-by, expires-at, supersedes-id nullable, schema-version, restriction-policy-snapshot jsonb).
- `verification_documents` — one row per uploaded document (storage key, content type, scan status, uploaded-at, purge-after nullable).
- `verification_state_transitions` — append-only ledger (verification-id, prior state, new state, actor, actor-kind, reason, metadata jsonb, occurred-at). Mirrors selected fields into `audit_log_entries` via the existing `IAuditEventPublisher`.
- `verification_market_schemas` — versioned per-market schema (market_code, version, effective-from, effective-to nullable, required_fields jsonb, allowed_document_types jsonb, retention_months int, cooldown_days int, expiry_days int, reminder_windows_days jsonb, sla_decision_business_days int, sla_warning_business_days int).
- `verification_reminders` — one row per reminder emission (verification-id, window-days int, emitted-at). Enforces FR-019 no-duplicate.

A `verification_eligibility_cache` materialized projection (Postgres `CREATE TABLE` with conflict-on-update, not a true materialized view) caches the per-customer rollup: `{ customer_id, market_code, eligibility_class, expires_at, computed_at }`. Invalidated synchronously inside every state transition's transaction. Read-side query (FR-021) joins this projection with `IProductRestrictionPolicy`; the bloom filter caching pattern from spec 004 is **not** needed here — the query is small-cardinality and indexed.

**Testing**: xUnit + FluentAssertions + `WebApplicationFactory<Program>` integration harness. Testcontainers Postgres (per spec 003 contract — no SQLite shortcut). Contract tests assert HTTP shape parity between every `spec.md` Acceptance Scenario and the live handler. Property tests for state-machine invariants (no terminal→non-terminal, no double-decision, idempotent transitions). Concurrency tests for FR-016 (two reviewers, single decision wins) using `Parallel.ForEachAsync`. Time-driven worker tests use `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing).

**Target Platform**: Backend-only in this spec. `services/backend_api/` ASP.NET Core 9 modular monolith. No Flutter, no Next.js — Phase 1C specs deliver UI against the contracts merged here.

**Project Type**: .NET vertical-slice module under the modular monolith (ADR-023).

**Performance Goals**:
- **Eligibility query**: p95 ≤ 5 ms in-process (locks SC-004; Catalog list pages can call once per restricted SKU per page-load). p99 ≤ 15 ms. Measured at the `IRequestHandler` boundary.
- **Reviewer queue list**: p95 ≤ 600 ms with 5,000 pending items, default-page (50) (SC-002 supports this with the "<2s detail load" constraint).
- **Reviewer detail load**: p95 ≤ 1500 ms with up to 5 documents and full transition history (≤50 transitions) (SC-002).
- **Submission write path**: p95 ≤ 800 ms (excludes upload latency, which is dominated by `IStorageService` + AV scan).

**Constraints**:
- **Idempotency**: every decision endpoint requires `Idempotency-Key` header (per spec 003 cross-cutting policy); duplicates within 24 h return the original 200 response, not a 409.
- **Concurrency guard**: every state-transitioning command uses an EF Core `RowVersion` (xmin) optimistic-concurrency check; the loser sees `verification.already_decided`. (FR-004, FR-016.)
- **PII at rest**: license-number values stored as plain TEXT (not encrypted column-level — Azure Postgres TDE covers at-rest; column-level encryption is deferred to spec 028 if required by a future audit). Document blobs live in storage, not the database (FR-006).
- **PII in logs**: `ILogger` destructuring filters block `LicenseNumber`, `DocumentStorageKey`, `RegulatorIdentifier`. Failure to redact a property is a build-time test failure (Serilog filter test from spec 003 reused).
- **PII reads audited**: every read of a `VerificationDocument` body and every read of `LicenseNumber` for terminal-state verifications goes through an audited `IPiiAccessRecorder` (FR-015a-e).
- **Worker idempotency**: expiry / reminder / purge workers are safe to re-run within a single window — FR-019 guarantees no duplicate reminders; expiry transition is a no-op if already in `expired`; purge is a no-op past `purge_after`.
- **Time source**: every state transition reads `TimeProvider.System.GetUtcNow()`; tests inject `FakeTimeProvider`. No `DateTime.UtcNow` in this module.

**Scale/Scope**: ~14 HTTP endpoints (8 customer, 6 admin), 44 functional requirements (FR-001–FR-039 with 5 alphas: FR-006a, FR-006b, FR-015a, FR-016a, FR-016b), 11 SCs, 6 key entities, 1 state machine, 5 tables + 1 projection, 3 hosted workers. Target capacity: 5,000 pending verifications per market, 200 concurrent submission writes, 50 concurrent reviewer queue reads, 1M eligibility-query calls per day across catalog/cart/checkout.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Unauth visitors still browse, search, and view restricted-SKU prices; eligibility hook gates only add-to-cart and checkout — matches the spec's User Story 3. | PASS |
| P4 Arabic / RTL editorial | Every customer-facing string (form labels, validation errors, decision-reason rendering, push/email/SMS template, PDF) is ICU-keyed AR + EN; reviewer-entered reasons surface to the customer's locale per FR-033. AR strings flagged for editorial review on PR. | PASS |
| P5 Market Configuration | All limits driven by `VerificationMarketSchema` rows (retention, cool-down, expiry, reminder windows, SLA target, required fields, allowed document types). Code reads schema by version snapshot; no hardcoded EG/KSA branches. | PASS |
| P6 Multi-vendor-ready | `Verification.RestrictionPolicySnapshot` jsonb captures `vendor_id` slot; reviewer-queue filter pre-built but defaulted to `null`; `IProductRestrictionPolicy` already exposes `vendor_id` (per 005). FR-036. | PASS |
| P8 Restricted Products | Single eligibility-query interface (`ICustomerVerificationEligibilityQuery`); catalog (005), cart (009), and checkout (010) MUST consume it; reason codes are a documented enum. | PASS |
| P19 Notifications | Verification publishes domain events; spec 025 subscribes. Verification state writes never block on notification success. Reminder windows configured per-market per FR-019. | PASS |
| P22 Fixed Tech | .NET 9, PostgreSQL 16, EF Core 9, MediatR per ADR-003. No deviation. | PASS |
| P23 Architecture | Vertical slice under `Modules/Verification/`; reuses existing seams (`IStorageService`, `IVirusScanService`, `IAuditEventPublisher`, RBAC). No premature service extraction. | PASS |
| P24 State Machines | One explicit state machine documented in `data-model.md` with allowed states, transitions, triggers, actors, failure handling, retries. No vague status fields. | PASS |
| P25 Audit | Every transition + every "open historical document" + every license-number read in terminal-state context emits an audit event with actor, timestamp, prior/new state (or read scope), reason, and structured metadata. | PASS |
| P27 UX Quality | No UI here, but error payloads carry stable reason codes (`verification.required`, `verification.expired`, `verification.cooldown_active`, etc.) so spec 014 (customer app) and 015/019 (admin) render correctly across loading/empty/error/success/restricted states. | PASS |
| P28 AI-Build Standard | Contracts file enumerates every endpoint's request/response/errors, every reason code, every state transition. No "support this somehow" wording. | PASS |
| P29 Required Spec Output | Goal, roles, rules, flow, states, data model, validation, API, edge cases, acceptance, phase, deps — all present in spec.md (verified by `checklists/requirements.md`). | PASS |
| P30 Phasing | Phase 1D Milestone 7. No scope creep into Phase 1.5 (regulator API, deeper analytics, customer notification preference UI). | PASS |
| P31 Constitution Supremacy | No conflict. | PASS |
| ADR-001 Monorepo | Code lands under `services/backend_api/Modules/Verification/`. No new repo. | PASS |
| ADR-003 Vertical slice | One folder per feature slice under `Customer/` and `Admin/`, each with Request / Validator / Handler / Endpoint / Tests. | PASS |
| ADR-004 EF Core 9 | Code-first migrations under `Modules/Verification/Persistence/Migrations/`. Soft-delete not used (verifications are immutable; documents purge via worker, audit trail preserved). `SaveChangesInterceptor` audit hook from spec 003 reused. `ManyServiceProvidersCreatedWarning` suppressed in `AddDbContext` per project memory. | PASS |
| ADR-010 KSA residency | All tables live in the KSA-region Postgres; no cross-region replication introduced. | PASS |

**No violations**. Complexity Tracking below documents intentional non-obvious design choices, not violations.

### Post-design re-check (after Phase 1 artifacts)

Re-evaluated after `data-model.md`, `contracts/verification-contract.md`, `quickstart.md`, and `research.md` were authored. **No new violations introduced.** Specific re-checks:

- **P5**: every limit, window, schema, and SLA value is sourced from `verification_market_schemas` (data-model §2.4) — not from code. ✅
- **P8**: `ICustomerVerificationEligibilityQuery` (contract §4.1) is the sole authority; `EligibilityReasonCode` enum (data-model §4) is documented and emitted to OpenAPI. ✅
- **P24**: state machine fully specified in `data-model.md §3` with 9 states, every allowed transition, every guard, and every forbidden path explicitly listed. ✅
- **P25**: audit-event schema (`data-model.md §5`) defines the five `event_kind` values; `IPiiAccessRecorder` chokepoint (contract §4.5) ensures every PII read is recorded. ✅
- **P28**: every endpoint in `contracts/verification-contract.md §2 / §3` has request, response, and reason-code documented; reason-code enum (§7) is exhaustive. ✅
- **P6**: `VendorId` slot reserved in `IProductRestrictionPolicy` (contract §4.3) with V1 = always null; `restriction_policy_snapshot` jsonb on `verifications` carries it forward. ✅

## Project Structure

### Documentation (this feature)

```text
specs/phase-1D/020-verification/
├── plan.md                 # This file
├── research.md             # Phase 0 — eligibility-cache shape, business-day calc, document purge model, regulator-extension stub, reminder de-dup, concurrency guard
├── data-model.md           # Phase 1 — 5 tables + 1 projection, 1 state machine, ERD
├── contracts/
│   └── verification-contract.md   # Phase 1 — every customer + admin endpoint, every reason code, every domain event
├── quickstart.md           # Phase 1 — implementer walkthrough, first slice, eligibility-hook smoke
├── checklists/
│   └── requirements.md     # quality gate (passes; see file)
└── tasks.md                # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
services/backend_api/
├── Modules/
│   ├── Shared/
│   │   ├── ICustomerVerificationEligibilityQuery.cs         # NEW — eligibility hook contract (consumed by 005/009/010)
│   │   ├── IProductRestrictionPolicy.cs                     # NEW — owned by spec 005, declared here so 020 can consume without cycle (per project memory: cross-module hooks via Modules/Shared/)
│   │   ├── ICustomerAccountLifecycleSubscriber.cs           # NEW — receives "account locked / deleted" from Identity; verification subscribes
│   │   └── VerificationDomainEvents.cs                      # NEW — VerificationApproved, Rejected, InfoRequested, Revoked, Expired, ReminderDue (subscribed by spec 025)
│   ├── Verification/                                         # NEW MODULE
│   │   ├── VerificationModule.cs                            # AddVerificationModule(IServiceCollection): DI, MediatR scan, AddDbContext (ManyServiceProvidersCreatedWarning suppressed per project memory)
│   │   ├── Primitives/
│   │   │   ├── VerificationState.cs                         # enum: Submitted, InReview, Approved, Rejected, InfoRequested, Expired, Revoked, Superseded, Void
│   │   │   ├── VerificationActorKind.cs                     # enum: Customer, Reviewer, System
│   │   │   ├── VerificationReasonCode.cs                    # enum + ICU-key mapper for every customer-visible code
│   │   │   ├── VerificationStateMachine.cs                  # transition rules + guard predicates
│   │   │   ├── VerificationMarketPolicy.cs                  # value-object resolved from VerificationMarketSchema row
│   │   │   ├── BusinessDayCalculator.cs                     # market-aware Sun–Thu, configurable holidays placeholder (empty list V1)
│   │   │   └── EligibilityReasonCode.cs                     # enum used by ICustomerVerificationEligibilityQuery
│   │   ├── Customer/
│   │   │   ├── SubmitVerification/                          # POST /api/customer/verifications
│   │   │   ├── GetMyActiveVerification/                     # GET  /api/customer/verifications/active
│   │   │   ├── ListMyVerifications/                         # GET  /api/customer/verifications
│   │   │   ├── GetMyVerification/                           # GET  /api/customer/verifications/{id}
│   │   │   ├── AttachDocument/                              # POST /api/customer/verifications/{id}/documents (only in submitted/info-requested)
│   │   │   ├── ResubmitWithInfo/                            # POST /api/customer/verifications/{id}/resubmit (info-requested → in-review)
│   │   │   └── RequestRenewal/                              # POST /api/customer/verifications/renew (within reminder window only)
│   │   ├── Admin/
│   │   │   ├── ListVerificationQueue/                       # GET  /api/admin/verifications  (filters; verification.review)
│   │   │   ├── GetVerificationDetail/                       # GET  /api/admin/verifications/{id}  (verification.review)
│   │   │   ├── DecideApprove/                               # POST /api/admin/verifications/{id}/approve  (verification.review)
│   │   │   ├── DecideReject/                                # POST /api/admin/verifications/{id}/reject  (verification.review)
│   │   │   ├── DecideRequestInfo/                           # POST /api/admin/verifications/{id}/request-info  (verification.review)
│   │   │   ├── DecideRevoke/                                # POST /api/admin/verifications/{id}/revoke  (verification.revoke)
│   │   │   └── OpenHistoricalDocument/                      # GET  /api/admin/verifications/{id}/documents/{documentId}/open  (terminal-state audited read; verification.review)
│   │   ├── Eligibility/
│   │   │   ├── CustomerVerificationEligibilityQuery.cs      # ICustomerVerificationEligibilityQuery impl (reads cache + IProductRestrictionPolicy)
│   │   │   └── EligibilityCacheInvalidator.cs               # invoked synchronously inside every transition's Tx
│   │   ├── Workers/
│   │   │   ├── VerificationExpiryWorker.cs                  # daily; moves expired approvals to Expired; emits VerificationExpired
│   │   │   ├── VerificationReminderWorker.cs                # daily; emits VerificationReminderDue (one per window per verification)
│   │   │   └── VerificationDocumentPurgeWorker.cs           # daily; deletes documents past purge_after; preserves entity + audit
│   │   ├── Authorization/
│   │   │   └── VerificationPermissions.cs                   # constants for verification.review, verification.revoke, verification.read_pii (granted to spec 019 customer-account admin role)
│   │   ├── Hooks/
│   │   │   └── AccountLifecycleHandler.cs                   # ICustomerAccountLifecycleSubscriber → void in-flight verifications + flip eligibility to inactive
│   │   ├── Entities/
│   │   │   ├── Verification.cs
│   │   │   ├── VerificationDocument.cs
│   │   │   ├── VerificationStateTransition.cs
│   │   │   ├── VerificationMarketSchema.cs
│   │   │   ├── VerificationReminder.cs
│   │   │   └── VerificationEligibilityCache.cs              # projection row
│   │   ├── Persistence/
│   │   │   ├── VerificationDbContext.cs                     # ManyServiceProvidersCreatedWarning suppressed (project-memory rule)
│   │   │   ├── Configurations/                              # IEntityTypeConfiguration<T> per entity
│   │   │   └── Migrations/
│   │   ├── Messages/
│   │   │   ├── verification.en.icu                          # ICU keys, EN
│   │   │   ├── verification.ar.icu                          # ICU keys, AR (editorial-grade)
│   │   │   └── AR_EDITORIAL_REVIEW.md                       # tracked Arabic strings pending editorial sign-off
│   │   └── Seeding/
│   │       ├── VerificationReferenceDataSeeder.cs           # market schemas (KSA, EG) — Dev+Staging+Prod (idempotent)
│   │       └── VerificationDevDataSeeder.cs                 # synthetic submissions across every state (Dev only, SeedGuard)
└── tests/
    └── Verification.Tests/
        ├── Unit/                                            # state machine, business-day calc, reason-code mapper, eligibility-cache invalidation
        ├── Integration/                                     # WebApplicationFactory + Testcontainers Postgres; every customer + admin slice; concurrency guard; worker behavior with FakeTimeProvider
        └── Contract/                                        # asserts every Acceptance Scenario from spec.md against live handlers
```

**Structure Decision**: Vertical-slice module under the modular monolith (ADR-003 / ADR-023), mirroring the layout established by spec 004 (`Modules/Identity/`) and 008 (`Modules/Inventory/`). The eligibility-query contract lives in `Modules/Shared/` so that catalog (005), cart (009), and checkout (010) consume it without taking a dependency on `Modules/Verification/` itself — matching the project-memory rule "cross-module hooks via Modules/Shared/ to avoid module dependency cycles". Customer and Admin slice trees are siblings to enforce visibly that they consume the same state machine but expose disjoint surfaces (no customer can hit an admin endpoint and vice versa, gated at the route + permission level).

## Implementation Phases

The `/speckit-tasks` run will expand each phase into dependency-ordered tasks. Listed here so reviewers can sanity-check ordering before tasks generation.

| Phase | Scope | Blockers cleared |
|---|---|---|
| A. Primitives | `VerificationState` enum + `VerificationStateMachine` + `VerificationActorKind` + `VerificationReasonCode` + `EligibilityReasonCode` + `BusinessDayCalculator` + `VerificationMarketPolicy` | Foundation for all slices |
| B. Persistence + migrations | 5 entities + cache projection + EF configurations + initial migration; `VerificationDbContext` with `ManyServiceProvidersCreatedWarning` suppression | Unblocks all slices and workers |
| C. Reference seeder | `VerificationReferenceDataSeeder` (KSA + EG market schemas, idempotent across all envs) | Unblocks integration tests + Staging/Prod boot |
| D. Customer slices | submit → list → get → attach-document → resubmit-with-info → request-renewal | FR-005 / FR-007 / FR-008 / FR-010 |
| E. Admin slices | queue → detail → approve / reject / request-info / revoke / open-historical-document | FR-011..FR-016, FR-015a |
| F. Eligibility query + cache | `ICustomerVerificationEligibilityQuery` impl + `EligibilityCacheInvalidator` wired into every transition's Tx | FR-021..FR-024, SC-004, SC-008 |
| G. Account-lifecycle hook | `AccountLifecycleHandler` consuming `ICustomerAccountLifecycleSubscriber`; void in-flight verifications + flip eligibility | FR-038, Edge Case "account locked / deleted" |
| H. Workers | `VerificationExpiryWorker` + `VerificationReminderWorker` + `VerificationDocumentPurgeWorker`, all under `IHostedService` with `FakeTimeProvider`-friendly seam | FR-017..FR-020, FR-006a, SC-005, SC-009 |
| I. Domain events + 025 contract | `VerificationDomainEvents` published on every transition + reminder; spec 025 subscribes (lands in spec 025's PR, not here) | FR-034 / FR-035 |
| J. Authorization wiring | `verification.review`, `verification.revoke`, `verification.read_pii` permission constants + `[RequirePermission]` attributes; spec 019 wires `verification.read_pii` into the customer-account admin role on its PR | FR-015 / FR-015a |
| K. Contracts + OpenAPI | Regenerate `openapi.verification.json`; assert contract test suite green; document every reason code | Guardrail #2 |
| L. AR/EN editorial | All customer-facing strings ICU-keyed; AR strings flagged in `AR_EDITORIAL_REVIEW.md` per project pattern | P4 |
| M. Integration / DoD | Full Testcontainers run; concurrency-guard load test (Story 2 scenario 7); time-driven worker tests; fingerprint; DoD checklist; audit-log spot-check script | PR gate |

## Complexity Tracking

> Constitution Check passed without violations. The rows below are *intentional non-obvious design choices* captured so future maintainers don't undo them accidentally.

| Design choice | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| `verification_eligibility_cache` projection (not a Postgres materialized view) | FR-021 + SC-004 require deterministic, low-latency, transactionally-consistent answers. A real materialized view can't be refreshed inside the same Tx as the state transition. | Recomputing eligibility from base tables on every Catalog list page risks N+1 + cross-Tx visibility windows (a customer just approved sees "ineligible" on their next page-load until the view refreshes). Bloom-filter caching (used by spec 004) overkill — eligibility cardinality is bounded. |
| `RestrictionPolicySnapshot` jsonb on `verifications` | A reviewer must see the policy that applied at submission (FR-026) — the policy can drift after submission. Snapshotting it on the row keeps the audit-replay deterministic. | Joining live `IProductRestrictionPolicy` at read time would let a future product-policy edit retroactively rewrite history visible to reviewers. |
| Five distinct decision endpoints (`approve`, `reject`, `request-info`, `revoke`, `open-historical-document`) instead of one polymorphic `decide` | Each carries different RBAC (`verification.review` vs `verification.revoke`), different idempotency semantics, different domain-event payload, and different audit shape. Distinct routes make this explicit at the auth + audit + contract layer. | A single `POST /decide` endpoint with a body `{ action: "approve" \| "reject" \| ... }` collapses three orthogonal concerns into runtime branching, weakening the reviewability of the audit trail. |
| `verification.read_pii` lives in this spec but is *granted* to a role defined in spec 019 | Permissions are owned by the module that enforces them; role composition is owned by the module that defines roles (spec 015 / 019). This keeps the permission identifier authoritative here while not reaching into 019's role model. | Defining the role in 020 couples roles to a single enforcing module; cross-spec composition becomes lossy. |
| Reminder de-duplication via a `verification_reminders` row (not via a job-state field on `verifications`) | A worker outage that re-runs an already-fired window would otherwise fire a duplicate; FR-019 forbids it. A separate row with a unique index `(verification_id, window_days)` makes "did we already fire this window?" a UNIQUE-constraint check rather than mutable-state arithmetic. | Tracking last-emitted-window on `verifications.last_reminder_window_days` invites race conditions and makes back-window skip-with-audit-note (Edge Case) hard to express. |
| SLA timer pauses while in `info-requested` | A verification waiting on the customer is not the reviewer's queue age. Counting it as such would mis-fire breach signals after every info-request. | Counting wall-clock from submission breaches FR-039's intent — "queue age" measures reviewer responsibility, not customer responsibility. |
| Documents purged on a daily worker (not synchronously when the window elapses) | Synchronous purge requires a per-row scheduler; daily-worker idempotent sweep is simpler, fault-tolerant, and matches FR-006a's "after the window elapses" wording. | A scheduler service for per-document timers adds infrastructure no other module needs in V1. |
| State machine encoded in code (`VerificationStateMachine.cs`), not in the database | Transition rules are part of the typed contract; database-driven state machines invite "edit a row, change behavior" failures and weaken compile-time guarantees. | A `state_transitions` definition table in Postgres centralizes data but defeats compiler-checked transition guards and reflection-based reviewer UI generation. |
| One `Verification` entity per submission, with `supersedes_id` for renewals (no separate `verification_renewal` table) | A renewal is operationally identical to a new submission with a back-pointer; doubling the entity model would force every consumer to handle two shapes of "approved verification". | Separate `verification_renewals` table forks the eligibility query and the reviewer queue UI for no behavioral gain. |
| Customer license-number values stored as plain TEXT, with column-level encryption deferred | Azure Postgres TDE covers at-rest; column-level encryption requires KMS rotation tooling that this spec doesn't justify. PDPL/EG-PDP review is the trigger for column-level encryption — flagged in spec 028's audit scope. | Eager column-level encryption now would build a key-rotation surface used by no other module in 1D. |
| Multi-vendor `vendor_id` slot reserved (jsonb in `RestrictionPolicySnapshot`, nullable on read paths) but never populated in launch | P6 multi-vendor-readiness without paying schema-migration cost in Phase 2 (matches spec 004's `role_scope = vendor` pattern). | Omitting the slot now forces a migration of `verifications` + the eligibility projection when vendor-scoped restricted SKUs land. |
