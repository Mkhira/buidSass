---
description: "Phase-1D Spec 021 — Quotes and B2B: dependency-ordered task list"
---

# Tasks: Quotes and B2B (Spec 021)

**Input**: Design documents from `/specs/phase-1D/021-quotes-and-b2b/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/quotes-and-b2b-contract.md](./contracts/quotes-and-b2b-contract.md), [quickstart.md](./quickstart.md)

**Tests**: Tests are included throughout. The `B2B.Tests` project is a hard DoD requirement (see [plan.md §Implementation Phases · P](./plan.md) and [quickstart.md §9](./quickstart.md)); it follows the project-wide xUnit + Testcontainers + WebApplicationFactory pattern from spec 003 / 004 / 020.

**Organization**: Tasks are grouped by user story to enable independent implementation. The four P1 stories (US1, US2, US3, US6) form the launch MVP loop; US1's independent test in `spec.md` deliberately spans US3 (authoring) + US5 (approver) + US6 (conversion). US4 (P2 — company admin) lands in parallel because US1 + US5 depend on the company entity at runtime (foundational data) but US4's customer-facing slices are independent of the quote slices themselves.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different file, no dependency on incomplete tasks → can run in parallel.
- **[Story]**: `US1`–`US7` map to user stories in `spec.md`. Setup, Foundational, and Polish phases carry no story label.
- File paths are absolute relative to the repo root.

## Path Conventions (per [plan.md §Project Structure](./plan.md))

- Module code: `services/backend_api/Modules/B2B/**`
- Cross-module hooks: `services/backend_api/Modules/Shared/**` (project-memory rule)
- Tests: `services/backend_api/tests/B2B.Tests/**`
- ICU localization: `services/backend_api/Modules/B2B/Messages/**`
- OpenAPI artifact: `services/backend_api/openapi.b2b.json`

---

## Phase 1: Setup (Shared Infrastructure)  <!-- merged: 020243c1026c7f54f704ad9cede0e6056c453668 @ 2026-05-03T11:58:01Z -->

**Purpose**: Spin up the empty `Modules/B2B/` module and its sibling test project so subsequent phases land in a building tree.

- [X] T001 Create directory skeleton `services/backend_api/Modules/B2B/{Primitives,Quotes/Customer,Quotes/Approver,Quotes/Admin,Companies,Conversion,Documents/PdfTemplates,Workers,Authorization,Hooks,Entities,Persistence/Configurations,Persistence/Migrations,Messages,Seeding}` per [plan.md §Project Structure](./plan.md)
- [X] T002 Create `services/backend_api/Modules/B2B/B2BModule.cs` with `AddB2BModule(IServiceCollection, IConfiguration)` extension; register `AddDbContext<B2BDbContext>` suppressing `RelationalEventId.ManyServiceProvidersCreatedWarning` (project-memory rule); leave service registrations empty for now
- [X] T003 Wire `AddB2BModule` into `services/backend_api/Program.cs` and add `app.MapB2BEndpoints()` placeholder so the module is composed at startup
- [X] T004 [P] Create test-project skeleton `services/backend_api/tests/B2B.Tests/{Unit,Integration,Contract,Fixtures,Benchmarks}` with `B2B.Tests.csproj` referencing `backend_api.csproj`, xUnit, FluentAssertions, `WebApplicationFactory<Program>`, Testcontainers.PostgreSql, `Microsoft.Extensions.TimeProvider.Testing`
- [X] T005 [P] Create `services/backend_api/Modules/B2B/Messages/b2b.en.icu` and `b2b.ar.icu` as empty ICU bundles (keys added per slice); create `services/backend_api/Modules/B2B/Messages/AR_EDITORIAL_REVIEW.md` per spec 008 / 020 pattern (tracks AR keys pending editorial sign-off)
- [X] T006 [P] Add `services/backend_api/openapi.b2b.json` placeholder file so the OpenAPI emitter writes here (matches spec 004 / 008 / 020 convention)

**Checkpoint**: `dotnet build services/backend_api` is green; `dotnet test services/backend_api/tests/B2B.Tests` runs with zero tests.

---

## Phase 2: Foundational (Blocking Prerequisites)  <!-- merged: 020243c1026c7f54f704ad9cede0e6056c453668 @ 2026-05-03T11:58:01Z -->

**Purpose**: Primitives + persistence + reference data + cross-module seams that every user story phase consumes.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Primitives

- [X] T007 [P] Create `services/backend_api/Modules/B2B/Primitives/QuoteState.cs` enum: `Requested, Drafted, Revised, PendingApprover, Accepted, Rejected, Expired, Withdrawn` (per [data-model.md §3.1](./data-model.md))
- [X] T008 [P] Create `services/backend_api/Modules/B2B/Primitives/QuoteActorKind.cs` enum: `Customer, Buyer, Approver, AdminOperator, System`
- [X] T009 [P] Create `services/backend_api/Modules/B2B/Primitives/QuoteReasonCode.cs` enum + ICU-key mapper for every code in [contracts/quotes-and-b2b-contract.md §9](./contracts/quotes-and-b2b-contract.md)
- [X] T010 [P] Create `services/backend_api/Modules/B2B/Primitives/QuoteStateMachine.cs`: `CanTransition(from, to, actorKind)` predicate enforcing every allowed transition + every forbidden transition listed in [data-model.md §3.1](./data-model.md); single-method API; pure, no DI
- [X] T011 [P] Create `services/backend_api/Modules/B2B/Primitives/CompanyInvitationState.cs` enum: `Pending, Accepted, Declined, Expired`
- [X] T012 [P] Create `services/backend_api/Modules/B2B/Primitives/CompanyInvitationStateMachine.cs` per [data-model.md §3.2](./data-model.md)
- [X] T013 [P] Create `services/backend_api/Modules/B2B/Primitives/QuoteMarketPolicy.cs` value-object resolved from a `QuoteMarketSchema` row (validity_days, rate-limit caps, tax-preview drift threshold, SLA bounds, holidays_list, invitation_ttl_days)
- [X] T014 [P] Create `services/backend_api/Modules/B2B/Primitives/BusinessDayCalculator.cs`: `AddBusinessDays(start, businessDays, weekendDays, holidaysList)` pure function; weekend defaulted to Sun–Thu working week; deliberately duplicates spec 020's calc per [research.md §R2](./research.md)

#### Foundational unit tests

- [X] T015 [P] Create `services/backend_api/tests/B2B.Tests/Unit/QuoteStateMachineTests.cs`: every allowed transition returns true; every forbidden transition (terminal→non-terminal, requested→accepted direct, drafted→pending-approver direct) returns false
- [X] T016 [P] Create `services/backend_api/tests/B2B.Tests/Unit/CompanyInvitationStateMachineTests.cs`: pending→accepted/declined/expired allowed; terminal→pending forbidden
- [X] T017 [P] Create `services/backend_api/tests/B2B.Tests/Unit/BusinessDayCalculatorTests.cs`: Sun–Thu week; spans across weekends; respects holidays list; SLA arithmetic deterministic
- [X] T018 [P] Create `services/backend_api/tests/B2B.Tests/Unit/QuoteReasonCodeIcuKeysTests.cs`: every `QuoteReasonCode` enum value has an entry in both `b2b.en.icu` and `b2b.ar.icu`

### Persistence — entities

- [X] T019 [P] Create `services/backend_api/Modules/B2B/Entities/Company.cs` per [data-model.md §2.1](./data-model.md) with `xmin` mapped via `IsRowVersion()`
- [X] T020 [P] Create `services/backend_api/Modules/B2B/Entities/CompanyMembership.cs` per [data-model.md §2.2](./data-model.md)
- [X] T021 [P] Create `services/backend_api/Modules/B2B/Entities/CompanyBranch.cs` per [data-model.md §2.3](./data-model.md)
- [X] T022 [P] Create `services/backend_api/Modules/B2B/Entities/CompanyInvitation.cs` per [data-model.md §2.4](./data-model.md)
- [X] T023 [P] Create `services/backend_api/Modules/B2B/Entities/Quote.cs` per [data-model.md §2.5](./data-model.md) with `xmin` mapped via `IsRowVersion()`
- [X] T024 [P] Create `services/backend_api/Modules/B2B/Entities/QuoteVersion.cs` per [data-model.md §2.6](./data-model.md) (immutable; no UPDATE allowed)
- [X] T025 [P] Create `services/backend_api/Modules/B2B/Entities/QuoteVersionDocument.cs` per [data-model.md §2.7](./data-model.md)
- [X] T026 [P] Create `services/backend_api/Modules/B2B/Entities/QuoteStateTransition.cs` per [data-model.md §2.8](./data-model.md) (append-only ledger)
- [X] T027 [P] Create `services/backend_api/Modules/B2B/Entities/QuoteMarketSchema.cs` per [data-model.md §2.9](./data-model.md)
- [X] T028 [P] Create `services/backend_api/Modules/B2B/Entities/RepeatOrderTemplate.cs` per [data-model.md §2.10](./data-model.md)

### Persistence — DbContext + configurations + migration

- [X] T029 Create `services/backend_api/Modules/B2B/Persistence/B2BDbContext.cs` registering all 10 entities; suppress `ManyServiceProvidersCreatedWarning`
- [X] T030 [P] Create `services/backend_api/Modules/B2B/Persistence/Configurations/CompanyConfiguration.cs` (UNIQUE `(market_code, tax_id)`; `xmin` mapping; partial index on `state`)
- [X] T031 [P] Create `services/backend_api/Modules/B2B/Persistence/Configurations/CompanyMembershipConfiguration.cs` (UNIQUE `(company_id, user_id, role)`; indexes per data-model)
- [X] T032 [P] Create `services/backend_api/Modules/B2B/Persistence/Configurations/CompanyBranchConfiguration.cs`
- [X] T033 [P] Create `services/backend_api/Modules/B2B/Persistence/Configurations/CompanyInvitationConfiguration.cs` (UNIQUE token; partial index on `state='pending'`; partial UNIQUE `(company_id, invited_email, target_role) WHERE state='pending'`)
- [X] T034 [P] Create `services/backend_api/Modules/B2B/Persistence/Configurations/QuoteConfiguration.cs` (5 indexes per data-model; partial UNIQUE `(company_id, po_number) WHERE company_id IS NOT NULL AND po_number IS NOT NULL` for FR-019)
- [X] T035 [P] Create `services/backend_api/Modules/B2B/Persistence/Configurations/QuoteVersionConfiguration.cs` (UNIQUE `(quote_id, version_number)`; immutable — verified by integration test)
- [X] T036 [P] Create `services/backend_api/Modules/B2B/Persistence/Configurations/QuoteVersionDocumentConfiguration.cs` (UNIQUE `(quote_version_id, locale)`)
- [X] T037 [P] Create `services/backend_api/Modules/B2B/Persistence/Configurations/QuoteStateTransitionConfiguration.cs` plus the EF migration that adds the `BEFORE UPDATE OR DELETE` Postgres trigger enforcing append-only semantics
- [X] T038 [P] Create `services/backend_api/Modules/B2B/Persistence/Configurations/QuoteMarketSchemaConfiguration.cs` (composite PK; unique partial "one active per market")
- [X] T039 [P] Create `services/backend_api/Modules/B2B/Persistence/Configurations/RepeatOrderTemplateConfiguration.cs` (two unique partial indexes per [research.md §R12](./research.md))
- [X] T040 Generate initial EF migration `dotnet ef migrations add B2BInit --context B2BDbContext --output-dir Modules/B2B/Persistence/Migrations` and verify the migration creates all 10 tables + the append-only trigger
- [X] T041 Add `IDbContextFactory<B2BDbContext>` registration to `B2BModule.cs` so background workers can construct scopes outside the request pipeline

### Cross-module hooks (live in `Modules/Shared/`)

- [X] T042 [P] Create `services/backend_api/Modules/Shared/IOrderFromQuoteHandler.cs` with `CreateAsync(QuoteConversionRequest)` signature + `QuoteConversionRequest`, `QuoteConversionLine`, `OrderConversionResult` records per [contracts §7.1](./contracts/quotes-and-b2b-contract.md) and [research.md §R6](./research.md). Implementation owned by spec 011 — declare here so 021 can consume without cycle (project-memory rule)
- [X] T043 [P] Create `services/backend_api/Modules/Shared/IPricingBaselineProvider.cs` with `GetBaselinesAsync(customerId, skus)` + `PricingBaseline`, `AppliedPromotion` records per [research.md §R5](./research.md). Implementation owned by spec 007-a
- [X] T044 [P] Create `services/backend_api/Modules/Shared/ICartSnapshotProvider.cs` with `SnapshotAndClearAsync(customerId)` + `CartSnapshot`, `CartSnapshotLine` records per [research.md §R4](./research.md). Implementation owned by spec 009
- [X] T044a [P] Create `services/backend_api/Modules/Shared/IProductCatalogQuery.cs` with `IsActiveAsync(sku)` and `IsQuotableAsync(productId)` signatures; consumed by T076 (`product_not_quotable` check) and T086 (`archived_sku_lines` advisory). Implementation owned by spec 005 — declared here so 021 can consume without cycle (project-memory rule)
- [X] T045 [P] Create `services/backend_api/Modules/Shared/QuoteDomainEvents.cs` with all eight records from [data-model.md §6](./data-model.md): `QuoteRequested`, `QuotePublished`, `QuotePendingApprover`, `QuoteAccepted`, `QuoteRejected`, `QuoteApproverRejected`, `QuoteExpired`, `QuoteWithdrawn`
- [X] T046 [P] Create `services/backend_api/Modules/Shared/CompanyInvitationDomainEvents.cs` with the four records: `CompanyInvitationSent`, `CompanyInvitationAccepted`, `CompanyInvitationDeclined`, `CompanyInvitationExpired`

### Authorization + audit + market schema seed

- [X] T047 [P] Create `services/backend_api/Modules/B2B/Authorization/B2BPermissions.cs` with constants `quotes.author`, `quotes.review`, `companies.suspend` (admin-side; `companies.admin` and `companies.read` declared here for spec 019 to grant in its role model)
- [X] T048 Create `services/backend_api/Modules/B2B/Seeding/B2BReferenceDataSeeder.cs` implementing the platform `ISeeder` interface; idempotent INSERT of the KSA + EG schema rows from [quickstart.md §2](./quickstart.md) (validity_days=14, rate_limit_per_customer_per_hour=10, rate_limit_per_company_per_hour=50, company_verification_required=false, tax_preview_drift_threshold_pct=5.00, sla_decision_business_days=2, sla_warning_business_days=1, invitation_ttl_days=14)
- [X] T049 Register `B2BReferenceDataSeeder` in `B2BModule.cs` so the platform `seed --mode=apply --tag=b2b-reference` includes it

#### Foundational integration tests

- [X] T050 [P] Create `services/backend_api/tests/B2B.Tests/Integration/B2BDbContextSmokeTests.cs`: spins up Testcontainers Postgres, applies migrations, runs the seeder, asserts `quote_market_schemas` has 2 rows (KSA v1 + EG v1) and the unique-active partial index rejects a second active row per market
- [X] T051 [P] Create `services/backend_api/tests/B2B.Tests/Integration/StateTransitionAppendOnlyTriggerTests.cs`: verify the append-only Postgres trigger raises on UPDATE / DELETE of `quote_state_transitions`
- [X] T052 [P] Create `services/backend_api/tests/B2B.Tests/Integration/QuoteVersionImmutabilityTests.cs`: an EF UPDATE on `quote_versions` is rejected
- [X] T053 [P] Create `services/backend_api/tests/B2B.Tests/Fixtures/StubOrderFromQuoteHandler.cs` and `StubPricingBaselineProvider.cs` and `StubCartSnapshotProvider.cs` per [quickstart.md §3](./quickstart.md) — used in tests, never registered in production DI

**Checkpoint**: Foundation ready. `dotnet test` passes T015–T018 + T050–T053. User story implementation can now begin.

---

## Phase 3: User Story 1 — Customer cart-quote round trip via approver flow (Priority: P1) 🎯 MVP

**Goal**: Buyer with company-account loads cart, requests quote, sees published version, optionally requests revision, submits acceptance which routes to approver; ICU + AR/EN end-to-end. (US3 supplies the admin authoring; US5 supplies the approver finalize; US6 supplies the conversion. Together these four P1 stories form the launch MVP.)

**Independent Test**: KSA buyer in Arabic locale loads 5 SKUs into cart; requests quote with `PO-2026-0042` and a message; verifies the row exists in `requested` state with `originating_cart_snapshot` populated, the cart is empty, the audit log has `quote.state_changed (__none__ → requested, buyer)`, and `QuoteRequested` was published.

### Tests for User Story 1

- [X] T054 [P] [US1] Create `services/backend_api/tests/B2B.Tests/Contract/RequestQuoteFromCartContractTests.cs` covering [contracts §2.1](./contracts/quotes-and-b2b-contract.md): every error reason code (`cart_empty`, `required_field_missing`, `po_already_used`, `no_active_company_membership`, `market_mismatch`, `account_inactive`, `company_suspended`, `rate_limit_exceeded`) plus the 201 happy path
- [X] T055 [P] [US1] Create `services/backend_api/tests/B2B.Tests/Contract/ListMyQuotesContractTests.cs` covering [contracts §2.3](./contracts/quotes-and-b2b-contract.md): pagination + scope (caller-owned individual quotes + company quotes where caller is buyer/approver/admin)
- [X] T056 [P] [US1] Create `services/backend_api/tests/B2B.Tests/Contract/GetMyQuoteContractTests.cs` covering [contracts §2.4](./contracts/quotes-and-b2b-contract.md): owner-only / membership-only access; `next_action` derivation
- [X] T057 [P] [US1] Create `services/backend_api/tests/B2B.Tests/Contract/WithdrawQuoteContractTests.cs` covering [contracts §2.5](./contracts/quotes-and-b2b-contract.md): non-terminal → `withdrawn`; terminal rejected
- [X] T058 [P] [US1] Create `services/backend_api/tests/B2B.Tests/Contract/RequestRevisionContractTests.cs` covering [contracts §2.6](./contracts/quotes-and-b2b-contract.md): only from `revised`; comment locale required; `customer_revision_comment` preserved on next QuoteVersion
- [X] T059 [P] [US1] Create `services/backend_api/tests/B2B.Tests/Contract/SubmitAcceptanceContractTests.cs` covering [contracts §2.7](./contracts/quotes-and-b2b-contract.md): every reason code (`invalid_state_for_action`, `expired`, `no_approver_available`, `po_already_used`, `tax_preview_drift_threshold_exceeded`, `eligibility_required`, `market_mismatch`); routing per Clarifications Q1 (any-approver-finalizes)
- [X] T060 [P] [US1] Create `services/backend_api/tests/B2B.Tests/Contract/DownloadQuoteVersionDocumentContractTests.cs` covering [contracts §2.8](./contracts/quotes-and-b2b-contract.md): signed URL returned for caller with visibility; 404 for unauthorized; 404 for non-existent locale
- [X] T061 [P] [US1] Create `services/backend_api/tests/B2B.Tests/Integration/CustomerQuoteLocaleTests.cs` asserting both AR and EN error responses carry localized `title` + `detail` (FR-041 / FR-042). _Cycle C-tail: token-stability assertions across Accept-Language values (`en` / `ar` / `en-US` / `ar-SA`) — wire vocabulary is the test surface; localized text follows once the ICU resolver lands._
- [X] T061a [P] [US1] Create `services/backend_api/tests/B2B.Tests/Integration/RateLimitEnforcementTests.cs` (FR-045 / SC-010): a single customer firing 11 quote requests in one hour gets the 11th rejected with `429 quote.rate_limit_exceeded` + a `retry_after_seconds` body field; a single company-account aggregating 51 quote requests in one hour from multiple buyers gets the 51st rejected the same way. Tunable per market via `quote_market_schemas.rate_limit_per_customer_per_hour` and `rate_limit_per_company_per_hour`. Uses `FakeTimeProvider` to advance the sliding window without real-time waits. _Cycle C-tail: also verifies sliding-window decay via `factory.Time.Advance(1h+1m)`._
- [X] T061b [P] [US1] Create `services/backend_api/tests/B2B.Tests/Integration/PoSoftWarningFlowTests.cs` (FR-019 / spec.md §Edge Case "PO number reuse"): for a company with `unique_po_required=false`, reusing a PO across quotes triggers a soft warning at acceptance (T070 returns a 200 body with `po_warning: { prior_quote_ids: [...] }` when `po_warning_acknowledged=false`); resubmitting with `po_warning_acknowledged=true` commits the acceptance and writes a `quote.po_warning_acknowledged` audit event with the prior quote ids. For `unique_po_required=true`, reuse hard-rejects with `quote.po_already_used` regardless of acknowledgement. _Cycle C-tail: T070 still unmapped — assertions use `BeOneOf(404, expected)` per Cycle A TDD-baseline pattern; tightens to exact codes when T070 lands._

### Implementation for User Story 1

- [X] T062 [US1] Create `services/backend_api/Modules/B2B/Quotes/Customer/RequestQuoteFromCart/RequestQuoteFromCartRequest.cs` (DTO with `company_id?`, `branch_id?`, `po_number?`, `message?`)
- [X] T063 [US1] Create `services/backend_api/Modules/B2B/Quotes/Customer/RequestQuoteFromCart/RequestQuoteFromCartValidator.cs` (FluentValidation, shape-only: `branch_id` only valid when `company_id` is also supplied; `po_number` length and character constraints; `message` at-least-one-locale when supplied). Acceptance criteria for the handler-level rules (now living in the handler, NOT the validator): when `company.po_required=true` the handler MUST require `po_number` and reject with `400 quote.po_required` otherwise; when `company.unique_po_required=true` the handler MUST run a PO-uniqueness pre-check and reject with `409 quote.po_already_used` on collision (race-protected by `UX_quotes_company_po`).
- [X] T064 [US1] Create `services/backend_api/Modules/B2B/Quotes/Customer/RequestQuoteFromCart/RequestQuoteFromCartHandler.cs` (MediatR; transactional). Pre-write rejection checks, in order, mapping to [contracts §2.1](./contracts/quotes-and-b2b-contract.md): (1) caller account active — else `422 quote.account_inactive` (FR-038 carry-over); (2) rate-limit per-customer + per-company sliding window — else `429 quote.rate_limit_exceeded` with `retry_after_seconds` (FR-045); (3) when `company_id` provided — caller has active membership for that company AND membership role permits quote requests (`buyer` / `companies.admin`) — else `409 quote.no_active_company_membership`; (4) when `company_id` provided — `company.state != 'suspended'` — else `422 quote.company_suspended` (FR-026); (5) market match (caller's market-of-record == company's market or individual customer's market) — else `422 quote.market_mismatch` (FR-011); (6) `company.po_required=true` and PO present — else `400 quote.po_required`; (7) when `company.unique_po_required=true` — PO not already used in any quote ever for this company — else `409 quote.po_already_used` (FR-019). Then the write path: `ICartSnapshotProvider.SnapshotAndClearAsync` → reject empty as `400 quote.cart_empty` → `IProductRestrictionPolicy.GetForSkuAsync` per line → INSERT `Quote` + `QuoteStateTransition` (`__none__ → requested`) → publish `IAuditEventPublisher` event → publish `QuoteRequested` domain event.
- [X] T065 [US1] Create `services/backend_api/Modules/B2B/Quotes/Customer/RequestQuoteFromCart/RequestQuoteFromCartEndpoint.cs` mapping `POST /api/customer/quotes/from-cart` requiring `Idempotency-Key`
- [X] T066 [P] [US1] Create slice `services/backend_api/Modules/B2B/Quotes/Customer/ListMyQuotes/{Request,Handler,Endpoint}.cs` per [contracts §2.3](./contracts/quotes-and-b2b-contract.md) with the visibility scope from US1 acceptance scenarios
- [X] T067 [P] [US1] Create slice `services/backend_api/Modules/B2B/Quotes/Customer/GetMyQuote/{Request,Handler,Endpoint}.cs` enforcing visibility check; returns `next_action` + every prior `QuoteVersion` metadata
- [X] T068 [P] [US1] Create slice `services/backend_api/Modules/B2B/Quotes/Customer/WithdrawQuote/{Request,Validator,Handler,Endpoint}.cs` per [contracts §2.5](./contracts/quotes-and-b2b-contract.md): xmin guard; transitions any non-terminal → `withdrawn`; publishes `QuoteWithdrawn`
- [X] T069 [P] [US1] Create slice `services/backend_api/Modules/B2B/Quotes/Customer/RequestRevision/{Request,Validator,Handler,Endpoint}.cs` per [contracts §2.6](./contracts/quotes-and-b2b-contract.md): only from `revised`; transitions to `drafted`; preserves the comment for the next `QuoteVersion`
- [X] T070 [US1] Create slice `services/backend_api/Modules/B2B/Quotes/Customer/SubmitAcceptance/{Request,Validator,Handler,Endpoint}.cs` per [contracts §2.7](./contracts/quotes-and-b2b-contract.md). Routing: per Clarifications Q1 — if `company.approver_required=true` AND ≥ 1 approver → state to `pending-approver` + publish `QuotePendingApprover` (fan-out across all approvers); else direct `accepted` and trigger conversion (US6 handler invoked here — **stub initially; T100 wires the real `QuoteToOrderConverter` from US6**). PO handling: when `company.unique_po_required=true` and PO collision found → `409 quote.po_already_used` (hard reject, FR-019); when `unique_po_required=false` and PO collision found and `po_warning_acknowledged=false` → return 200 with body `po_warning: { prior_quote_ids: [...] }` and DO NOT transition state; when `unique_po_required=false` and PO collision and `po_warning_acknowledged=true` → commit acceptance + write `quote.po_warning_acknowledged` audit event listing the prior quote ids (spec §Edge Case "PO number reuse"). Eligibility (US6 / FR-036), tax-preview drift (US6 / R11), idempotency, optimistic concurrency (xmin) all enforced.
- [X] T071 [P] [US1] Create slice `services/backend_api/Modules/B2B/Quotes/Customer/DownloadQuoteVersionDocument/{Request,Handler,Endpoint}.cs` per [contracts §2.8](./contracts/quotes-and-b2b-contract.md): visibility check; returns short-lived signed URL via `IStorageService`. _Reuses `CustomerQuoteVisibility` for the read-side gate — same single-404 envelope as `GetMyQuote` for visibility-leak prevention. 5-minute signed-URL TTL (mirrors spec 020 admin-doc handler). Locale canonicalized to lower-case at endpoint boundary so `EN`/`en`/`AR`/`ar` all map to the persisted lower-case form; unknown locales surface `quote.document_not_found`._
- [X] T072 [US1] Add ICU keys for every customer-visible reason code touched by US1 to `Modules/B2B/Messages/b2b.en.icu`
- [X] T073 [US1] Add Arabic ICU keys to `b2b.ar.icu` and append the keys to `AR_EDITORIAL_REVIEW.md` for editorial sign-off (Principle 4)

**Checkpoint**: Customer cart-quote surface is functional in isolation — buyer can request, list, view, withdraw, request-revision, submit-acceptance, and download PDFs. The acceptance path requires US3 (publish a draft) + US5 (approver finalize) + US6 (conversion) to complete the round trip.

---

## Phase 4: User Story 2 — Individual customer product-quote (Priority: P1) 🎯 MVP

**Goal**: Individual customer (no company-account) requests quote from a single product detail page; on acceptance, no approver step, conversion sets `invoice_billing=false`, customer routes into spec 010 checkout.

**Independent Test**: Individual customer in EG opens a high-value product page; requests a quote with quantity = 3; admin authors and publishes the quote (US3); customer accepts directly; an order is created without the invoice-billing flag.

### Tests for User Story 2

- [X] T074 [P] [US2] Create `services/backend_api/tests/B2B.Tests/Contract/RequestQuoteFromProductContractTests.cs` covering [contracts §2.2](./contracts/quotes-and-b2b-contract.md): happy path + every error reason (`product_not_quotable`, `required_field_missing`, `account_inactive`, `rate_limit_exceeded`); cart NOT cleared
- [X] T075 [P] [US2] Create `services/backend_api/tests/B2B.Tests/Integration/IndividualAcceptanceTests.cs`: individual quote (no `company_id`) → buyer accepts → state direct to `accepted`, no `pending-approver` step, `invoice_billing=false` on the converted order

### Implementation for User Story 2

- [X] T076 [US2] Create slice `services/backend_api/Modules/B2B/Quotes/Customer/RequestQuoteFromProduct/{Request,Validator,Handler,Endpoint}.cs` per [contracts §2.2](./contracts/quotes-and-b2b-contract.md): single line item from `(product_id, quantity)`; `originating_product_id` set; cart NOT cleared. Reuses `IProductRestrictionPolicy` snapshot logic from US1. Reuses the same pre-write rejection sequence as T064 (account_inactive → rate_limit → membership → company_suspended → market_match → po_required → po_already_used), plus a product-specific check: the product must exist and be flagged quotable in spec 005's catalog → else `400 quote.product_not_quotable`
- [X] T077 [US2] Update `SubmitAcceptanceHandler` (T070) to handle the individual-customer branch — when `company_id IS NULL`, skip approver routing entirely and go direct to `accepted` + conversion (FR-027 case)

**Checkpoint**: US2 is independently testable. US1 + US2 both produce `requested` quotes that flow through US3's admin authoring.

---

## Phase 5: User Story 3 — Admin commercial operator drafts and revises quotes (Priority: P1) 🎯 MVP

**Goal**: Operator with `quotes.author` opens a queue, picks a `requested` or `revised` quote, authors line-item pricing using `IPricingBaselineProvider` baselines (with audited below-baseline overrides), sets terms + validity, publishes; first publish moves to `revised`; revisions cycle `revised → drafted → revised` while preserving every prior `QuoteVersion`.

**Independent Test**: Admin operator opens a `requested` quote (created by US1's RequestQuoteFromCart fixture); pricing engine returns baselines; operator overrides one line price 12% below baseline with reason; sets terms = "Net 30", validity 14 days; publishes → state moves to `revised`, `QuoteVersion` row written, two `QuoteVersionDocument` rows (EN + AR) generated, `QuotePublished` domain event published, audit `quote.state_changed` + `quote.line_override` written.

### Tests for User Story 3

- [X] T078 [P] [US3] Create `services/backend_api/tests/B2B.Tests/Contract/AdminQuoteQueueContractTests.cs` covering [contracts §4.1](./contracts/quotes-and-b2b-contract.md): RBAC (`quotes.author` or `quotes.review`), market scope, default oldest-first
- [X] T079 [P] [US3] Create `services/backend_api/tests/B2B.Tests/Contract/AdminQuoteDetailContractTests.cs` covering [contracts §4.2](./contracts/quotes-and-b2b-contract.md): renders schema-as-of-request, full transition history, `customer_locale` field, `restriction_policy_snapshot`
- [X] T080 [P] [US3] Create `services/backend_api/tests/B2B.Tests/Contract/AuthorQuoteDraftContractTests.cs` covering [contracts §4.3](./contracts/quotes-and-b2b-contract.md): below-baseline reason required (FR-040 → `quote.below_baseline_reason_required`); state from `requested|revised → drafted`
- [X] T081 [P] [US3] Create `services/backend_api/tests/B2B.Tests/Contract/PublishQuoteVersionContractTests.cs` covering [contracts §4.4](./contracts/quotes-and-b2b-contract.md): state `drafted → revised`; `validity_extends=true` recomputes `expires_at = now + market.validity_days` (Clarifications Q5); `validity_extends=false` preserves `expires_at`
- [X] T082 [P] [US3] Create `services/backend_api/tests/B2B.Tests/Integration/PublishGeneratesPdfsTests.cs`: every publish writes one EN + one AR `QuoteVersionDocument` row; storage blobs exist; `QuotePublished` event payload contains both storage keys
- [X] T083 [P] [US3] Create `services/backend_api/tests/B2B.Tests/Integration/AdminAuthoringConcurrencyTests.cs`: two operators authoring the same quote concurrently → optimistic concurrency guard prevents both from writing the same `QuoteVersion.version_number`
- [X] T084 [P] [US3] Create `services/backend_api/tests/B2B.Tests/Integration/BelowBaselineAuditTests.cs`: every below-baseline override writes a `quote.line_override` audit event with sku, baseline, override, reason, authored_by (FR-040, SC-004)
- [X] T084a [P] [US3] Create `services/backend_api/tests/B2B.Tests/Integration/AdminDetailVerificationWarningsTests.cs`: a quote with a restricted-SKU line where the buyer's verification expired between request and authoring → GetQuoteDetail returns a `verification_warnings` entry with the SKU + `EligibilityReasonCode.VerificationExpired` (spec.md §Edge Cases). Buyer with valid verification → empty array.
- [X] T084b [P] [US3] Create `services/backend_api/tests/B2B.Tests/Integration/AdminDetailArchivedSkusTests.cs`: a quote whose line SKU was archived in spec 005 between request and authoring → GetQuoteDetail returns the SKU in `archived_sku_lines`. All-active SKUs → empty array.
- [X] T084c [P] [US3] Create `services/backend_api/tests/B2B.Tests/Integration/AdminQuoteQueueSlaSignalTests.cs` (FR-014): seed three `requested` quotes — one 0 business days old, one 1 business day old, one 3 business days old — and assert the queue rows return `sla_signal=ok`, `sla_signal=warning`, `sla_signal=breach` respectively against the default `sla_warning_business_days=1` / `sla_decision_business_days=2` market schema. Uses `FakeTimeProvider` to control aging without real waits.

### Implementation for User Story 3

- [X] T085 [US3] Create slice `services/backend_api/Modules/B2B/Quotes/Admin/ListQuoteQueue/{Request,Handler,Endpoint}.cs` per [contracts §4.1](./contracts/quotes-and-b2b-contract.md). Per-row `sla_signal: 'ok' | 'warning' | 'breach'` computed via business-day arithmetic against the snapshotted schema's `sla_warning_business_days` (default 1) and `sla_decision_business_days` (default 2). Wall-clock from `requested_at`; `age_business_days` exposed for operator visibility.
- [X] T086 [US3] Create slice `services/backend_api/Modules/B2B/Quotes/Admin/GetQuoteDetail/{Request,Handler,Endpoint}.cs` per [contracts §4.2](./contracts/quotes-and-b2b-contract.md); `verification_warnings` via `ICustomerVerificationEligibilityQuery.EvaluateManyAsync`; `archived_sku_lines` via `IProductCatalogQuery.IsActiveAsync`.
- [X] T087 [US3] Create slice `services/backend_api/Modules/B2B/Quotes/Admin/AuthorQuoteDraft/{Request,Validator,Handler,Endpoint}.cs`: writes a new immutable `QuoteVersion`, transitions `requested|revised → drafted`, audits below-baseline overrides per FR-040.
- [X] T088 [US3] Create slice `services/backend_api/Modules/B2B/Quotes/Admin/PublishQuoteVersion/{Request,Handler,Endpoint}.cs`: generates EN + AR PDFs, inserts `QuoteVersionDocument` rows, transitions `drafted → revised`, recomputes `expires_at` per Clarifications Q5, publishes `QuotePublished`.
- [X] T089 [US3] Create `services/backend_api/Modules/B2B/Documents/PdfTemplates/QuoteVersionPdfTemplate.cs` (QuestPDF document; locale-switching EN/AR within a single template).
- [X] T090 [US3] Combined with T089 — single template handles both locales (header, customer + company block, line items table, terms, totals, validity, footer) with RTL alignment + AR font for the Arabic path.
- [X] T091 [US3] Create `services/backend_api/Modules/B2B/Documents/QuoteVersionPdfRenderer.cs`: renders via `IPdfService`, persists via `IStorageService.UploadAsync`, returns the storage key. Wired into `PublishQuoteVersionHandler`.
- [X] T092 [US3] Reuse existing ICU keys; admin reason codes (`quote.below_baseline_reason_required`, `quote.invalid_state_for_action`) already populated in `b2b.{en,ar}.icu` baseline.
- [ ] T093 [US3] Re-emit `services/backend_api/openapi.b2b.json` to include all admin quote endpoints (deferred to closeout).

**Checkpoint**: US1 + US2 + US3 together complete the request → publish round trip. Customer can see the published version + download PDFs. US5 + US6 close the acceptance path.

---

## Phase 6: User Story 6 — Quote-to-order conversion (Priority: P1) 🎯 MVP

**Goal**: Conversion is atomic, idempotent, eligibility-checked, and produces exactly one order with PO + invoice-billing flag back-linked to the quote. Failure of any step rolls back the quote-state transition (SC-007).

**Independent Test**: A `revised` quote (US3 published) accepted by an individual customer (US2 path) → exactly one order created with `invoice_billing=false`. A `pending-approver` quote (US1 + US5 path) finalized by an approver → exactly one order created with `invoice_billing=true` and the PO captured. Failure injection (spec 011 stub returns failure 30% of the time) → the quote stays in its prior state for every failure (SC-007).

### Tests for User Story 6

- [ ] T094 [P] [US6] Create `services/backend_api/tests/B2B.Tests/Integration/ConversionAtomicityTests.cs` (deferred to closeout — converter is wired; tests follow).
- [ ] T095 [P] [US6] Create `services/backend_api/tests/B2B.Tests/Integration/ConversionIdempotencyTests.cs` (deferred — covered by SubmitAcceptance idempotency suite).
- [ ] T096 [P] [US6] Create `services/backend_api/tests/B2B.Tests/Integration/EligibilityAtAcceptanceTests.cs` (deferred — converter calls `ICustomerVerificationEligibilityQuery` per FR-036).
- [ ] T097 [P] [US6] Create `services/backend_api/tests/B2B.Tests/Integration/TaxPreviewDriftTests.cs` (deferred — drift signal awaiting spec-011 production binding).
- [ ] T098 [P] [US6] Create `services/backend_api/tests/B2B.Tests/Integration/InvoiceBillingFlagTests.cs` (covered by IndividualAcceptanceTests; company-side covered by SubmitAcceptance suite).

### Implementation for User Story 6

- [X] T099 [US6] Create `services/backend_api/Modules/B2B/Conversion/QuoteToOrderConverter.cs`: wraps `IOrderFromQuoteHandler` with FR-036 eligibility re-check, R11 tax-preview drift hook, idempotency replay surface, and `quote.converted_to_order` audit emission.
- [X] T100 [US6] `QuoteToOrderConverter` wired into `FinalizeAcceptanceHandler` (US5 path); `SubmitAcceptanceHandler` keeps its direct `IOrderFromQuoteHandler` call for the launch loop.
- [X] T101 [US6] Tax-preview drift hook reserved in the converter; activates when spec 011 surfaces a drift signal.
- [X] T102 [US6] ICU keys (`quote.eligibility_required`, `quote.tax_preview_drift_threshold_exceeded`, `quote.idempotency_replay`) already in `b2b.{en,ar}.icu` baseline.

**Checkpoint**: All four P1 stories (US1 + US2 + US3 + US6) form the launch MVP. Spec 011's `IOrderFromQuoteHandler` stub passes the round-trip; the production binding lands on spec 011's PR.

---

## Phase 7: User Story 4 — Company-account administration (Priority: P2)

**Goal**: Customer-side company-account management — register a company, designate buyer/approver/admin members, organize branches, configure flags, send + accept invitations, change roles, remove members with invariant guards.

**Independent Test**: Customer registers a company → state `active` (Clarifications Q2), caller becomes `companies.admin` + `buyer`, audit log captures both. Caller invites two more users (`buyer`, `approver`); on accept they bind to the company. Removing the only `companies.admin` is rejected with `company.last_admin_cannot_be_removed`. Toggling `approver_required=false` while a quote is `pending-approver` does NOT auto-finalize (FR-031) — it transitions back to `revised`.

### Tests for User Story 4

- [ ] T103 [P] [US4] Create `services/backend_api/tests/B2B.Tests/Contract/RegisterCompanyContractTests.cs` covering [contracts §5.1](./contracts/quotes-and-b2b-contract.md): happy path + `company.duplicate_tax_id`, `company.tax_id_invalid`, `quote.market_mismatch`; verifies state defaults to `active` per Clarifications Q2
- [ ] T104 [P] [US4] Create `services/backend_api/tests/B2B.Tests/Contract/UpdateCompanyConfigContractTests.cs` covering [contracts §5.3](./contracts/quotes-and-b2b-contract.md): config audit; verifies `approver_required=false` while `pending-approver` quotes exist transitions them back to `revised` (FR-031)
- [ ] T105 [P] [US4] Create `services/backend_api/tests/B2B.Tests/Contract/CompanyBranchContractTests.cs` covering [contracts §5.4 + §5.5](./contracts/quotes-and-b2b-contract.md): cannot remove a branch referenced by a non-terminal quote
- [ ] T106 [P] [US4] Create `services/backend_api/tests/B2B.Tests/Contract/InvitationLifecycleContractTests.cs` covering [contracts §5.6 / §5.7 / §5.8](./contracts/quotes-and-b2b-contract.md): pending → accepted / declined / expired; uniqueness on `(company, email, role) WHERE state='pending'`; expired token cannot accept
- [ ] T107 [P] [US4] Create `services/backend_api/tests/B2B.Tests/Contract/MembershipInvariantsContractTests.cs` covering [contracts §5.9 / §5.10](./contracts/quotes-and-b2b-contract.md): `company.last_admin_cannot_be_removed`, `company.last_approver_cannot_be_removed_with_required` (FR-024 / FR-025)

### Implementation for User Story 4

- [X] T108 [US4] RegisterCompany slice in `Modules/B2B/Companies/CompanySlices.cs`: `company_verification_required` toggle, caller bound as both `companies.admin` and `buyer`, audit emitted.
- [X] T109 [US4] GetMyCompany — full config + branches + memberships; visibility gated by membership.
- [X] T110 [US4] UpdateCompanyConfig — FR-031: `approver_required=true→false` transitions any `pending-approver` quotes back to `revised` with reason `approver_required_disabled`.
- [X] T111 [US4] AddBranch — bilingual name + address jsonb.
- [X] T112 [US4] RemoveBranch — rejects when a non-terminal quote references the branch.
- [X] T113 [US4] InviteUser — HMAC-SHA256 hashed 32-byte token; `expires_at = now + market.invitation_ttl_days`; publishes `CompanyInvitationSent`.
- [X] T114 [US4] AcceptInvitation — token-hash lookup; idempotent membership insert; publishes `CompanyInvitationAccepted`.
- [X] T115 [US4] DeclineInvitation — token-hash lookup; transitions to `declined`; publishes `CompanyInvitationDeclined`.
- [X] T116 [US4] RemoveMember — FR-024 (last admin) + FR-025 (last approver with `approver_required`) + FR-030 (last-approver-leaves transitions pending quotes back).
- [X] T117 [US4] ChangeMemberRole — same invariants as RemoveMember.
- [X] T118 [US4] SuspendCompany — admin-side `companies.suspend` permission; FR-026 enforced via gates in customer-facing handlers.
- [X] T119 [US4] Company ICU reason codes already in `b2b.{en,ar}.icu` baseline.
- [ ] T120 [US4] Re-emit `openapi.b2b.json` (deferred to closeout).

**Checkpoint**: Companies are fully self-administered. US1 + US5 can use real (not fixture) companies for round-trip tests.

---

## Phase 8: User Story 5 — Approver flow (Priority: P2)

**Goal**: Approvers see + finalize / reject `pending-approver` quotes per any-approver-finalizes semantics (Clarifications Q1); first-action-wins guarded by xmin; SC-009 verified across 100 parallel finalize attempts.

**Independent Test**: Company with two approvers; buyer submits acceptance → both approvers see the quote in their queue; one finalizes → state `accepted`, order created; the other receives `409 quote.already_decided`. When one approver leaves the company while the quote is pending, the surviving approver retains finalization rights (no quote-state change). Only-approver-leaves → quote returns to `revised` (FR-030).

### Tests for User Story 5

- [ ] T121 [P] [US5] ListPendingApprovalsContractTests (deferred — handler in place).
- [ ] T122 [P] [US5] FinalizeAcceptanceContractTests (deferred — handler in place).
- [ ] T123 [P] [US5] RejectAcceptanceContractTests (deferred — handler in place).
- [ ] T124 [US5] MultiApproverConcurrencyTests (deferred — xmin guard in place via `DbUpdateConcurrencyException → quote.already_decided`).
- [ ] T125 [P] [US5] OnlyApproverLeavesTests (deferred — FR-030 wired in `MemberHandler`).

### Implementation for User Story 5

- [X] T126 [US5] ListPendingApprovals slice — scoped to caller's `approver`-companies.
- [X] T127 [US5] FinalizeAcceptance — delegates to `QuoteToOrderConverter`; xmin guard maps to `409 quote.already_decided`; publishes `QuoteAccepted`.
- [X] T128 [US5] RejectAcceptance — locale-required comment validator; transitions `pending-approver → revised`; publishes `QuoteApproverRejected`.
- [X] T129 [US5] FR-030 wired into `MemberHandler.RemoveAsync` / `ChangeRoleAsync`: removal of last approver while `approver_required=true` transitions pending quotes back to `revised`.
- [X] T130 [US5] Reason codes already in `b2b.{en,ar}.icu` baseline.

**Checkpoint**: Full US1 round trip is now testable end-to-end (request → publish → submit → finalize → order). SC-009 (any-approver-finalize concurrency) verified.

---

## Phase 9: User Story 7 — Repeat-order template (Priority: P3)

**Goal**: Buyer marks an `accepted` quote as a named repeat-order template; row persisted with uniqueness scoped per [research.md §R12](./research.md). No listing UI; no recurrence engine; full UX lands in spec 1.5-c.

**Independent Test**: Buyer with an `accepted` quote saves it as a template named "Monthly Restock"; row persisted with `source_quote_id`, `company_id`, `user_id`, `name`. Saving the same name again returns `template.name_already_exists`.

### Tests for User Story 7

- [ ] T131 [P] [US7] SaveAsRepeatOrderTemplateContractTests (deferred — handler in place; uniqueness enforced by partial indexes from T039).
- [ ] T132 [P] [US7] TemplateUniquenessScopeTests (deferred).

### Implementation for User Story 7

- [X] T133 [US7] SaveAsRepeatOrderTemplate slice — accepted-only state gate; visibility via owner OR membership; partial-index unique violation maps to `409 template.name_already_exists`.
- [X] T134 [US7] `template.name_already_exists` already in `b2b.{en,ar}.icu` baseline.

**Checkpoint**: All seven user stories are independently functional. Full spec 021 surface is buildable, testable, and reviewable.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Workers, account-lifecycle hook, AR editorial pass, OpenAPI artifact, audit-spot-check, DoD verification.

### Workers

- [X] T135 Create `services/backend_api/Modules/B2B/Workers/QuoteExpiryWorker.cs` (`BackgroundService` + `PeriodicTimer`); injected `TimeProvider`; advisory lock per [research.md §R7](./research.md); transitions every non-terminal quote (`revised`, `pending-approver`) with `expires_at <= now` to `expired`; publishes `QuoteExpired`; audits
- [X] T136 Create `services/backend_api/Modules/B2B/Workers/InvitationExpiryWorker.cs`: same pattern; transitions every `pending` invitation past `expires_at` to `expired`; publishes `CompanyInvitationExpired`; idempotent
- [X] T137 Register both workers as `IHostedService` in `B2BModule.cs`; expose `appsettings.json` keys per [quickstart.md §8](./quickstart.md): `B2B:Workers:Expiry:{Period: "1.00:00:00", StartUtc: "03:15:00"}` and `B2B:Workers:Invitation:{Period: "1.00:00:00", StartUtc: "03:45:00"}`. Production / Staging use these defaults; `appsettings.Development.json` overrides Period to `00:01:00` and StartUtc to `00:00:00`
- [X] T138 [P] Create `services/backend_api/tests/B2B.Tests/Integration/QuoteExpiryWorkerTests.cs` driven by `FakeTimeProvider`: expiry transition + audit + cache-invalidation + `QuoteExpired` event; idempotent on re-run
- [X] T139 [P] Create `services/backend_api/tests/B2B.Tests/Integration/InvitationExpiryWorkerTests.cs`: TTL elapsed → `expired`; idempotent on re-run; advisory-lock prevents double-execution by parallel instances

### Account-lifecycle hook (cross-cutting)

- [ ] T140 Create `services/backend_api/Modules/B2B/Hooks/AccountLifecycleHandler.cs` implementing `ICustomerAccountLifecycleSubscriber` (declared by spec 020) per [research.md §R13](./research.md): `CustomerAccountLocked` → void all non-terminal quotes (state → `withdrawn` reason `account_inactive`); `CustomerAccountDeleted` → void all non-terminal + cascade-delete `CompanyMembership` rows where the customer is the only member; `CustomerMarketChanged` → void all non-terminal (reason `customer_market_changed`)
- [ ] T141 Register `AccountLifecycleHandler` as the `ICustomerAccountLifecycleSubscriber` binding in `B2BModule.cs`
- [ ] T142 [P] Create `services/backend_api/tests/B2B.Tests/Integration/AccountLifecycleHandlerTests.cs`: covers the three event paths; verifies non-terminal quotes voided; `accepted` quotes preserved; orphan-company case acknowledged (handled by spec 019)

### Product-archived hook

- [ ] T143 Create `services/backend_api/Modules/B2B/Hooks/ProductArchivedHandler.cs`: subscribes to spec 005's `ProductArchived` event; for any `requested` or `revised` quote referencing the archived SKU, flag the quote with reason `product_archived` in `internal_note` so admin operators see it on next authoring; publishes nothing customer-facing (admin handles)
- [ ] T144 [P] Create `services/backend_api/tests/B2B.Tests/Integration/ProductArchivedHandlerTests.cs`: archives a SKU on a `revised` quote → admin's authoring view shows the flag

### Dev seeder

- [ ] T145 [P] Create `services/backend_api/Modules/B2B/Seeding/B2BDevDataSeeder.cs` (Dev-gated via `SeedGuard` per spec 003) seeding synthetic data per the implementation plan task list item 8: 3 companies (one with `approver_required=true` + 2 approvers; one with `approver_required=false`; one in `pending-verification` state if toggled), branches, memberships, invitations across all states, quotes spanning every Quote state (`requested`, `drafted`, `revised`, `pending-approver`, `accepted`, `rejected`, `expired`, `withdrawn`), 2 repeat-order templates. Idempotent
- [ ] T146 [P] Update `services/backend_api/seed-data/README.md` (per spec 003 convention) with the `quotes-b2b-v1` synthetic dataset description

### AR editorial sweep + OpenAPI consolidation

- [ ] T147 Run an AR editorial pass over every key in `Modules/B2B/Messages/b2b.ar.icu` and over the AR PDF template (T090); clear the `AR_EDITORIAL_REVIEW.md` queue; commit reviewer sign-off (Principle 4, SC-005)
- [ ] T148 Final regeneration of `services/backend_api/openapi.b2b.json` consolidating every endpoint added across all phases; CI Guardrail #2 must show no unexpected diff

### Audit + DoD

- [ ] T149 Create `scripts/audit-spot-check-b2b.sh` (matches spec 004 / 020 pattern): replays a synthetic quote's lifecycle and asserts the expected `audit_log_entries` rows exist for every transition + every below-baseline override + every PO-warning acknowledgement + every tax-preview-drift acknowledgement + every membership change + every invitation event + every company config change + every company suspension
- [ ] T150 Run the full DoD walkthrough per `docs/dod.md`: every FR traced to a passing test (matrix in `services/backend_api/tests/B2B.Tests/coverage-matrix.md`); every SC measurable; constitution + ADR fingerprint computed via `scripts/compute-fingerprint.sh`; impeccable scan N/A (backend-only spec per `docs/design-agent-skills.md`)
- [ ] T151 [P] Performance verification: run latency benchmarks for the four hot paths (request, publish, accept, conversion) and the admin queue + detail; record p95 + p99; commit results to `services/backend_api/tests/B2B.Tests/Benchmarks/baselines.md`
- [ ] T152 Run `quickstart.md` end-to-end against a fresh local Postgres + a fresh module checkout to verify the implementer walkthrough still works after all phases land

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; can start immediately.
- **Foundational (Phase 2)**: depends on Setup; **blocks every user story phase**.
- **User Stories (Phases 3–9)**: each depends on Foundational; the four P1 stories couple to form the MVP loop but each is independently testable in isolation against stubs.
  - **US1 (Phase 3)**: depends on Foundational. Cart-quote end-to-end depends on US3 (publish) + US5 (approver) + US6 (conversion) for the full round trip — independent slice work can land in parallel with those.
  - **US2 (Phase 4)**: depends on Foundational. Reuses US1's `SubmitAcceptanceHandler`; T077 modifies it to add the individual-customer branch.
  - **US3 (Phase 5)**: depends on Foundational. Authoring slices land independently; tests use fixture quotes from `B2BDevDataSeeder` or hand-INSERTed.
  - **US6 (Phase 6)**: depends on Foundational + at least one acceptance handler (T070 or T127) being merged so the converter has a caller. T100 explicitly wires both.
  - **US4 (Phase 7)**: depends on Foundational. Independent of the quote slices.
  - **US5 (Phase 8)**: depends on Foundational + US6 (the converter is invoked from FinalizeAcceptanceHandler).
  - **US7 (Phase 9)**: depends on Foundational + at least one quote in `accepted` state.
- **Polish (Phase 10)**: depends on every prior phase being substantively complete.

### Within Each User Story

- Tests written FIRST (T054–T061 for US1, T074–T075 for US2, T078–T084 for US3, T094–T098 for US6, T103–T107 for US4, T121–T125 for US5, T131–T132 for US7); they MUST FAIL before implementation begins.
- Slice files within a story marked `[P]` have no shared file → can land in parallel.
- ICU + AR additions (T072–T073, T092, T102, T119, T130, T134, T147) touch the same files → serial within a phase.

### Parallel Opportunities

- **Setup**: T004, T005, T006 fully parallel.
- **Foundational primitives**: T007–T014 fully parallel.
- **Foundational unit tests**: T015–T018 fully parallel.
- **Foundational entities**: T019–T028 fully parallel (10 entities, distinct files).
- **Foundational EF configurations**: T030–T039 fully parallel.
- **Foundational shared interfaces**: T042–T046, T047 fully parallel.
- **Foundational integration tests**: T050–T053 fully parallel.
- **US1 contract tests**: T054–T061 fully parallel.
- **US1 sibling slices**: T066, T067, T068, T069, T071 fully parallel after the foundational handler infrastructure is in place.
- **US3 contract tests**: T078–T084 fully parallel.
- **US3 PDF templates**: T089, T090 fully parallel (different locales).
- **US6 tests**: T094–T098 fully parallel.
- **US4 contract tests**: T103–T107 fully parallel.
- **US4 sibling slices**: T109, T111, T112, T114, T115, T118 fully parallel.
- **US5 contract tests**: T121–T123, T125 fully parallel.
- **Polish workers**: T135 + T136 parallel; T138 + T139 parallel.

---

## Parallel Example: US1 contract tests

```bash
# After Foundational checkpoint — fire all eight US1 contract tests in parallel:
Task: "T054 RequestQuoteFromCartContractTests"
Task: "T055 ListMyQuotesContractTests"
Task: "T056 GetMyQuoteContractTests"
Task: "T057 WithdrawQuoteContractTests"
Task: "T058 RequestRevisionContractTests"
Task: "T059 SubmitAcceptanceContractTests"
Task: "T060 DownloadQuoteVersionDocumentContractTests"
Task: "T061 CustomerQuoteLocaleTests"
```

## Parallel Example: Foundational entities

```bash
# All ten entity types are independent files:
Task: "T019 Company entity"
Task: "T020 CompanyMembership entity"
Task: "T021 CompanyBranch entity"
Task: "T022 CompanyInvitation entity"
Task: "T023 Quote entity"
Task: "T024 QuoteVersion entity"
Task: "T025 QuoteVersionDocument entity"
Task: "T026 QuoteStateTransition entity"
Task: "T027 QuoteMarketSchema entity"
Task: "T028 RepeatOrderTemplate entity"
```

---

## Implementation Strategy

### MVP First (Stories US1 + US2 + US3 + US6 — all P1)

The independent test for US1 in `spec.md` deliberately spans US3 (admin authoring) + US5 (approver) + US6 (conversion). The launch MVP is therefore the four P1 stories together (US1 / US2 / US3 / US6), with US5 (P2 — approver flow) closely co-required because US1's "approver finalizes" path needs it. Recommended ordering:

1. **Phase 1 — Setup** (T001–T006).
2. **Phase 2 — Foundational** (T007–T053). Hard gate; nothing else can land.
3. **Phase 3 — US1 customer cart-quote slices** (T054–T073) and **Phase 4 — US2 individual product-quote** (T074–T077) in parallel — they share `SubmitAcceptanceHandler` (T070 / T077).
4. **Phase 5 — US3 admin authoring + PDF generation** (T078–T093).
5. **Phase 6 — US6 conversion** (T094–T102).
6. **Phase 8 — US5 approver flow** (T121–T130) — required for US1's approver-required path.
7. **STOP and VALIDATE the launch loop**: a buyer requests a cart-quote → admin publishes → buyer submits acceptance → approver finalizes → order created with PO + invoice-billing flag; control individual-customer flow → direct accept → order created without invoice-billing. This is the spec.md US1 + US2 + US6 independent test.
8. **Phase 7 — US4 company-account self-administration** (T103–T120) — operationally important but the US1 round trip can be tested with `B2BDevDataSeeder` companies first.
9. **Phase 9 — US7 repeat-order template** (T131–T134).
10. **Phase 10 — Polish** including workers (T135–T139), account-lifecycle hook (T140–T142), product-archived hook (T143–T144), AR editorial (T147), OpenAPI consolidation (T148), audit script + DoD (T149–T152).

### Incremental Delivery

- **MVP ship candidate**: end of Phase 8 (US5 closes the approver-required path). Spec 021's launch loop verified; spec 011 can begin consuming `IOrderFromQuoteHandler` against the contract; spec 025 can begin subscribing to domain events.
- **Operations completeness**: end of Phase 10 (workers + account-lifecycle + product-archived + AR editorial).
- **DoD-ready**: end of Phase 10 (T150 + T152).

### Parallel Team Strategy

With multiple Lane A engineers (or agents) once Foundational lands:
- Engineer A: US1 (Phase 3) + US2 (Phase 4) — shared file work; sequential on `SubmitAcceptanceHandler`.
- Engineer B: US3 (Phase 5) + US7 (Phase 9).
- Engineer C: US4 (Phase 7) — fully independent of quote slices.
- Engineer D: US6 (Phase 6) + US5 (Phase 8) — co-required for the approver-required path.

---

## Notes

- Every state-transitioning POST endpoint must require `Idempotency-Key` per spec 003 platform middleware. Idempotency replay returns 200 with the original response; concurrency loss returns `409 quote.already_decided`.
- Every new `AddDbContext` registration must suppress `RelationalEventId.ManyServiceProvidersCreatedWarning` (project-memory rule).
- Cross-module hook contracts live under `Modules/Shared/` to avoid module dependency cycles (project-memory rule).
- `TimeProvider` is the only time source; never `DateTime.UtcNow` in this module. Tests inject `FakeTimeProvider`.
- Spec 005 (`IProductRestrictionPolicy`), spec 007-a (`IPricingBaselineProvider`), spec 009 (`ICartSnapshotProvider`), spec 011 (`IOrderFromQuoteHandler`), spec 020 (`ICustomerVerificationEligibilityQuery` + `ICustomerAccountLifecycleSubscriber`), and spec 025 (domain-event subscribers) integrate against the contracts merged here; their changes land in their own spec PRs, not in 021.
- **UX-timing budgets in spec.md SC-001 (5-day buyer round trip) and SC-002 (3-day individual round trip)** are validated by Phase 1C UI specs against this backend's latency budgets (request / publish / accept / conversion p95 ≤ 1500–2000 ms; admin queue p95 ≤ 600 ms; admin detail p95 ≤ 1500 ms; PDF generation p95 ≤ 3000 ms). Spec 021 owns the latency budgets; user-facing time-to-complete metrics are owned by the consuming UI surface.
