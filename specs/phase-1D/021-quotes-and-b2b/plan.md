# Implementation Plan: Quotes and B2B

**Branch**: `phase_1D_creating_specs` (working) · target merge: `021-quotes-and-b2b` | **Date**: 2026-04-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1D/021-quotes-and-b2b/spec.md`

## Summary

Deliver the Phase-1D B2B module that turns the constitutional Principle-9 mandate ("B2B is V1, not a future afterthought") into a single backend module covering both halves of the deliverable per the implementation plan:

1. **Quote lifecycle** (Principle 24): one explicit `Quote` state machine `requested → drafted → revised → (pending-approver) → (accepted | rejected | expired | withdrawn)`. Drafted is operator-only-visible; revised is customer-visible. Every published version is preserved as an immutable `QuoteVersion`. The customer surface delivers request-from-cart (US1) and request-from-product (US2); the admin surface delivers authoring + revisions; the conversion path produces exactly one order per accepted quote (atomic, idempotent — SC-003 / SC-007).
2. **Company-account model**: self-registered `Company` entities with `CompanyMembership` rows binding customer-accounts to one of three roles (`companies.admin`, `buyer`, `approver`); optional `CompanyBranch` sub-entities; `CompanyInvitation` flow with 14-day TTL.
3. **Approval flow** (the clarification-locked any-approver-finalizes semantics): when `approver_required=true` and ≥ 1 approver, all approvers receive parallel notifications; the first finalize action wins (optimistic-concurrency-guarded). When `approver_required=false`, buyer acceptance is final.
4. **Conversion contract**: declares `IOrderFromQuoteHandler` in `Modules/Shared/`; spec 011 implements. Conversion runs in a single transaction with `IProductRestrictionPolicy` + `ICustomerVerificationEligibilityQuery` (spec 020) checks, captures PO + invoice-billing flag, snapshots line items + totals, and back-links the order to the quote (FR-032 / FR-033 / FR-034 / FR-035 / FR-036).
5. **Bilingual quote PDFs**: every `QuoteVersion` publish renders one EN PDF and one AR PDF via the existing `Modules/Pdf` `IPdfService` (QuestPDF-based) + `Modules/Storage` `IStorageService`; persisted as `QuoteVersionDocument` rows; downloadable by buyer / approver / admin via signed URL.
6. **Repeat-order template stubs**: `RepeatOrderTemplate` rows persisted on customer demand; no listing UI, no recurrence engine, no scheduling — full UI lands in spec 1.5-c with no schema migration.
7. **Two background workers**: `QuoteExpiryWorker` (daily, transitions non-terminal quotes past `expires_at` to `expired`) and `InvitationExpiryWorker` (daily, expires `pending` invitations past their 14-day TTL).
8. **Market-aware schema** (Principle 5): `QuoteMarketSchema` rows define validity_days (default 14), rate-limit caps, company-verification-required toggle (default OFF for both KSA and EG), tax-preview drift threshold, and the SLA target — all editable per market without a code deploy.
9. **Multi-vendor readiness** (Principle 6): the `Quote` entity, `QuoteVersion` line-item snapshot, `IOrderFromQuoteHandler` contract, and `Company` model all reserve a future `vendor_id` slot; V1 sets it null. State machine, eligibility integration, and customer flow do not change when vendor-scoped quoting lands in Phase 2.

No UI ships in this spec. The customer + admin web UIs are owned by Phase 1C specs (014 / 015 / 018 / 019); spec 015's contract merge is the gate before Lane B begins.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (LTS), PostgreSQL 16 (per spec 004 + ADR-022).

**Primary Dependencies**:
- `MediatR` v12.x + `FluentValidation` v11.x — vertical-slice handlers (ADR-003).
- `Microsoft.EntityFrameworkCore` v9.x — code-first migrations (ADR-004).
- `Microsoft.AspNetCore.Authorization` (built-in) — `[RequirePermission("quotes.*")]` and `[RequirePermission("companies.*")]` attributes from spec 004's RBAC.
- `Modules/Pdf/IPdfService` (existing, QuestPDF-backed) — bilingual PDF rendering for `QuoteVersionDocument`.
- `Modules/Storage/IStorageService` (existing) — PDF persistence + signed-URL retrieval.
- `Modules/AuditLog/IAuditEventPublisher` (existing) — every state transition + every below-baseline price override + every membership change.
- `Modules/Identity` consumables — RBAC primitives + `ICustomerPostSignInHook` pattern; spec 020's `ICustomerAccountLifecycleSubscriber` for void-on-account-locked / market-changed.
- `Modules/Verification` consumables — `ICustomerVerificationEligibilityQuery` (consumed at acceptance time per FR-036).
- `Modules/Cart` consumables — declared cross-module hook `ICartSnapshotProvider` (declared here, implemented by spec 009) for "snapshot the buyer's current cart and clear it on quote-request" (FR-010).
- `Modules/Pricing` consumables — declared cross-module hook `IPricingBaselineProvider` (declared here, implemented by spec 007-a) for line-item baseline price + applicable promotions + tax preview at admin authoring time (FR-015).
- `Modules/Orders` integration — declared cross-module hook `IOrderFromQuoteHandler` (declared here, implemented by spec 011) for the atomic conversion contract.
- `MessageFormat.NET` (already vendored by spec 003) — ICU AR/EN keys for every customer-facing reason code.

**Storage**: PostgreSQL (Azure Saudi Arabia Central per ADR-010). 10 new tables in the `b2b` schema:
- `companies`, `company_memberships`, `company_branches`, `company_invitations` — company-account model.
- `quotes`, `quote_versions`, `quote_version_documents`, `quote_state_transitions` (append-only audit ledger), `quote_market_schemas` (versioned per-market policy), `repeat_order_templates`.

State writes use EF Core optimistic concurrency via Postgres `xmin` mapped as `IsRowVersion()` (the same pattern adopted in spec 020) for the multi-approver-finalize race (SC-009).

**Testing**: xUnit + FluentAssertions + `WebApplicationFactory<Program>` integration harness. Testcontainers Postgres (per spec 003 contract — no SQLite shortcut). Contract tests assert HTTP shape parity between every `spec.md` Acceptance Scenario and the live handler. Property tests for state-machine invariants (no terminal→non-terminal, no double-decision, idempotent transitions). Concurrency tests for FR-029 (two approvers, single finalize wins) using `Parallel.ForEachAsync`. Time-driven worker tests use `FakeTimeProvider`.

**Target Platform**: Backend-only in this spec. `services/backend_api/` ASP.NET Core 9 modular monolith. No Flutter, no Next.js — Phase 1C specs deliver UI against the contracts merged here.

**Project Type**: .NET vertical-slice module under the modular monolith (ADR-023).

**Performance Goals**:
- **Quote request submit (cart or product)**: p95 ≤ 1500 ms (excludes cart-snapshot IO; the snapshot is in-process via `ICartSnapshotProvider`).
- **Admin queue list**: p95 ≤ 600 ms with 5,000 pending quotes per market, default page (50).
- **Quote detail load**: p95 ≤ 1500 ms with up to 5 versions and full transition history (≤50 transitions).
- **Buyer acceptance write path** (no-approver-required path): p95 ≤ 2000 ms (includes the in-Tx call to spec 011's `IOrderFromQuoteHandler` and `ICustomerVerificationEligibilityQuery`).
- **Approver finalize write path**: same p95 ≤ 2000 ms.
- **PDF generation per version**: synchronous to publish; the EN+AR pair generates within p95 ≤ 3000 ms (QuestPDF + storage put). If the budget is breached on a specific theme, generation MAY be moved to an in-process `Channel<>`-backed background queue without changing the contract — but V1 ships synchronous.

**Constraints**:
- **Idempotency**: every state-transitioning POST endpoint requires `Idempotency-Key` (per spec 003 platform middleware); duplicates within 24 h return the original 200 response.
- **Concurrency guard**: every state-transitioning command uses an EF Core `RowVersion` (xmin) optimistic-concurrency check; the loser sees `quote.already_decided` (FR-004 / FR-029).
- **PII at rest**: company tax-id stored as plain TEXT (TDE covers at-rest); customer-supplied messages preserved verbatim. No column-level encryption introduced here.
- **PII in logs**: `ILogger` destructuring filters block `TaxId`, `RegulatorIdentifier`, `PoNumber`. Same pattern as spec 020.
- **Worker idempotency**: expiry workers safe to re-run within a window; no duplicate side effects (transitions are no-ops if already in terminal state).
- **Time source**: every state transition reads `TimeProvider.System.GetUtcNow()`; tests inject `FakeTimeProvider`. No `DateTime.UtcNow` in this module.
- **Quote PDFs are immutable per `QuoteVersion`**: regenerating the PDF after the version is published requires a new `QuoteVersion` (i.e. a new revision); the admin "regenerate PDF" action is a no-op against the same version unless an editorial defect fix is required (FR-018 read-strictly).

**Scale/Scope**: ~22 HTTP endpoints (9 customer, 3 approver, 4 admin, 8 company-account + 2 invitation-flow + 1 admin-side company-suspend declared here for spec 019). 46 functional requirements. 10 SCs. 7 key entities (+ derived `QuoteVersionDocument`). 2 state machines. 10 tables. 2 hosted workers. Target capacity: 500 quote requests / day (steady state across both markets), 5,000 pending quotes per market in admin queue, peaks of 50 concurrent admin authoring sessions, 200 concurrent buyer acceptance writes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Browse remains unauth; quote requests require auth (spec 004); customer flows do not gate browse. | PASS |
| P4 Arabic / RTL editorial | Every customer-facing string ICU-keyed AR + EN; bilingual PDFs (one EN, one AR) per version; reviewer reasons + admin authoring messages structured `{ en?, ar? }` per FR-042. | PASS |
| P5 Market Configuration | Validity, rate-limit caps, company-verification-required toggle, tax-preview drift threshold, SLA target — all in `quote_market_schemas` rows. No hardcoded EG/KSA branches. | PASS |
| P6 Multi-vendor-ready | `vendor_id` slot reserved on `Quote`, line-item snapshot, `IOrderFromQuoteHandler`, `Company`. V1 always null. | PASS |
| P9 B2B is V1 | Comprehensive coverage: company accounts, multi-user buyer/approver, branch hierarchy, PO numbers, invoice-billing terms, quote-request loop, approval flow, repeat-order templates. | PASS |
| P10 Pricing centralized | `IPricingBaselineProvider` (declared here, owned by 007-a) is the sole source of baseline prices + tax preview. Operator overrides require audited reason. | PASS |
| P17 Order / Payment / Fulfillment / Refund separated | Quote state stays distinct from order state. `QuoteAccepted → IOrderFromQuoteHandler` creates the order; downstream order/payment/fulfillment states are owned by 011 + 012 + 026. | PASS |
| P18 Tax invoices | `invoice_billing` flag on accepted-from-quote orders signals spec 012 to issue an invoice on Net-X terms (per `terms_days`). | PASS |
| P19 Notifications | Domain events listed in FR-043; spec 025 subscribes; quote-state writes never block on notification success. | PASS |
| P22 Fixed Tech | .NET 9, PostgreSQL 16, EF Core 9, MediatR — no deviation. | PASS |
| P23 Architecture | Vertical slice under `Modules/B2B/`; reuses existing seams (`IStorageService`, `IPdfService`, `IAuditEventPublisher`, RBAC). No premature service extraction. | PASS |
| P24 State Machines | Two explicit state machines (`Quote`, `CompanyInvitation`); each documented in `data-model.md` with allowed states, transitions, triggers, actors, failure handling. | PASS |
| P25 Audit | Every state transition + every below-baseline override + every membership change emits an audit event with actor, timestamp, prior/new state, structured metadata. | PASS |
| P27 UX Quality | No UI here, but error payloads carry stable reason codes (`quote.cooldown_active`, `quote.already_decided`, `quote.eligibility_required`, `quote.po_already_used`, etc.) for spec 014/015 to render. | PASS |
| P28 AI-Build Standard | Contracts file enumerates every endpoint's request / response / errors / reason codes. | PASS |
| P29 Required Spec Output | Goal, roles, rules, flow, states, data model, validation, API, edge cases, acceptance, phase, deps — all present in spec.md. | PASS |
| P30 Phasing | Phase 1D Milestone 7. Repeat-order template UI deliberately deferred to spec 1.5-c (FR-038). Quote PDFs in V1 (per Clarifications Q4). | PASS |
| P31 Constitution Supremacy | No conflict. | PASS |
| ADR-001 Monorepo | Code lands under `services/backend_api/Modules/B2B/`. | PASS |
| ADR-003 Vertical slice | One folder per feature slice under `Quotes/Customer/`, `Quotes/Approver/`, `Quotes/Admin/`, `Companies/`. | PASS |
| ADR-004 EF Core 9 | Code-first migrations under `Modules/B2B/Persistence/Migrations/`. `SaveChangesInterceptor` audit hook from spec 003 reused. `ManyServiceProvidersCreatedWarning` suppressed (project-memory rule). | PASS |
| ADR-010 KSA residency | All tables in the KSA-region Postgres; no cross-region replication. | PASS |

**No violations**. Complexity Tracking below documents intentional non-obvious design choices.

### Post-design re-check (after Phase 1 artifacts)

Re-evaluated after `data-model.md`, `contracts/quotes-and-b2b-contract.md`, `quickstart.md`, and `research.md` were authored. **No new violations introduced.** Specific re-checks:

- **P5**: every market-tunable knob (validity_days, rate-limit caps, company_verification_required, tax_preview_drift_threshold_pct, sla_*_business_days, holidays_list) is sourced from `quote_market_schemas` rows. ✅
- **P9**: every B2B requirement enumerated in the implementation plan task list (1–8) maps to specific FRs and tasks in this plan; no scope drop. ✅
- **P17**: state model in `data-model.md §3` is purely Quote-internal — no order/payment/fulfillment state mixed in; conversion handoff is a single in-Tx call to `IOrderFromQuoteHandler`. ✅
- **P19**: `QuoteDomainEvents.cs` + `CompanyInvitationDomainEvents.cs` are declared in `Modules/Shared/`; subscribed by spec 025; no in-line notification calls inside quote-state writes. ✅
- **P24**: `Quote` and `CompanyInvitation` state machines are encoded in code (`QuoteStateMachine.cs`, `CompanyInvitationStateMachine.cs`); transition guards visible at compile time. ✅
- **P25**: `audit_event_kinds` documented in `data-model.md §5` covering `quote.state_changed`, `quote.line_override`, `quote.po_warning_acknowledged`, `company.member_changed`, `company.invitation_*`, `quote.document_purged`. ✅
- **P28**: contracts file enumerates 22 endpoints + the 4 cross-module interfaces with full reason-code inventory (28 codes). ✅

## Project Structure

### Documentation (this feature)

```text
specs/phase-1D/021-quotes-and-b2b/
├── plan.md                  # This file
├── research.md              # Phase 0 — concurrency model, multi-approver fan-out, PDF retention, conversion atomicity, cart snapshot, pricing seam
├── data-model.md            # Phase 1 — 10 tables, 2 state machines, ERD
├── contracts/
│   └── quotes-and-b2b-contract.md   # Phase 1 — every customer + approver + admin + company-account endpoint, every reason code, every domain event
├── quickstart.md            # Phase 1 — implementer walkthrough, first slice, conversion smoke
├── checklists/
│   └── requirements.md      # quality gate (pass)
└── tasks.md                 # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
services/backend_api/
├── Modules/
│   ├── Shared/
│   │   ├── IOrderFromQuoteHandler.cs         # NEW — conversion contract; implemented by spec 011
│   │   ├── IPricingBaselineProvider.cs       # NEW — baseline price + tax preview per SKU; implemented by spec 007-a
│   │   ├── ICartSnapshotProvider.cs          # NEW — snapshot + clear current cart for a customer; implemented by spec 009
│   │   ├── QuoteDomainEvents.cs              # NEW — QuoteRequested/Published/Accepted/Rejected/Expired/Withdrawn/PendingApprover/ApproverRejected (subscribed by spec 025)
│   │   └── CompanyInvitationDomainEvents.cs  # NEW — CompanyInvitationSent/Accepted/Declined/Expired (subscribed by spec 025)
│   ├── B2B/                                   # NEW MODULE (combines quotes + companies)
│   │   ├── B2BModule.cs                      # AddB2BModule(IServiceCollection); MediatR scan; AddDbContext suppressing ManyServiceProvidersCreatedWarning
│   │   ├── Primitives/
│   │   │   ├── QuoteState.cs                 # enum: Requested, Drafted, Revised, PendingApprover, Accepted, Rejected, Expired, Withdrawn
│   │   │   ├── QuoteStateMachine.cs          # transition rules + guard predicates
│   │   │   ├── QuoteActorKind.cs             # enum: Customer, Buyer, Approver, AdminOperator, System
│   │   │   ├── QuoteReasonCode.cs            # enum + ICU-key mapper for every customer-visible reason
│   │   │   ├── CompanyInvitationState.cs     # enum: Pending, Accepted, Declined, Expired
│   │   │   ├── CompanyInvitationStateMachine.cs
│   │   │   ├── QuoteMarketPolicy.cs          # value-object resolved from QuoteMarketSchema row
│   │   │   └── BusinessDayCalculator.cs      # duplicates Spec 020's calc to avoid cross-module coupling; trivial pure function (R2)
│   │   ├── Quotes/
│   │   │   ├── Customer/
│   │   │   │   ├── RequestQuoteFromCart/
│   │   │   │   ├── RequestQuoteFromProduct/
│   │   │   │   ├── ListMyQuotes/
│   │   │   │   ├── GetMyQuote/
│   │   │   │   ├── WithdrawQuote/
│   │   │   │   ├── RequestRevision/
│   │   │   │   ├── SubmitAcceptance/
│   │   │   │   ├── DownloadQuoteVersionDocument/
│   │   │   │   └── SaveAsRepeatOrderTemplate/
│   │   │   ├── Approver/
│   │   │   │   ├── ListPendingApprovals/
│   │   │   │   ├── FinalizeAcceptance/
│   │   │   │   └── RejectAcceptance/
│   │   │   └── Admin/
│   │   │       ├── ListQuoteQueue/
│   │   │       ├── GetQuoteDetail/
│   │   │       ├── AuthorQuoteDraft/
│   │   │       └── PublishQuoteVersion/
│   │   ├── Companies/
│   │   │   ├── RegisterCompany/
│   │   │   ├── GetMyCompany/
│   │   │   ├── UpdateCompanyConfig/
│   │   │   ├── AddBranch/
│   │   │   ├── RemoveBranch/
│   │   │   ├── InviteUser/
│   │   │   ├── AcceptInvitation/
│   │   │   ├── DeclineInvitation/
│   │   │   ├── RemoveMember/
│   │   │   ├── ChangeMemberRole/
│   │   │   └── SuspendCompany/                  # admin action — declared here for spec 019 to invoke
│   │   ├── Conversion/
│   │   │   └── QuoteToOrderConverter.cs         # invokes IOrderFromQuoteHandler; runs eligibility hook; in-Tx with state transition
│   │   ├── Documents/
│   │   │   ├── QuoteVersionPdfRenderer.cs       # uses Modules/Pdf IPdfService + IStorageService
│   │   │   └── PdfTemplates/                    # QuestPDF document templates EN + AR
│   │   ├── Workers/
│   │   │   ├── QuoteExpiryWorker.cs             # daily; transitions non-terminal expired quotes
│   │   │   └── InvitationExpiryWorker.cs        # daily; expires pending invitations past 14-day TTL
│   │   ├── Authorization/
│   │   │   └── B2BPermissions.cs                # quotes.author, quotes.review, companies.admin (admin-side; in spec 019), companies.suspend
│   │   ├── Hooks/
│   │   │   ├── AccountLifecycleHandler.cs       # subscribes to ICustomerAccountLifecycleSubscriber from Spec 020 — voids in-flight quotes on locked/deleted/market-changed
│   │   │   └── ProductArchivedHandler.cs        # responds to product archival → flags affected quotes (Edge Case)
│   │   ├── Entities/
│   │   │   ├── Company.cs
│   │   │   ├── CompanyMembership.cs
│   │   │   ├── CompanyBranch.cs
│   │   │   ├── CompanyInvitation.cs
│   │   │   ├── Quote.cs
│   │   │   ├── QuoteVersion.cs
│   │   │   ├── QuoteVersionDocument.cs
│   │   │   ├── QuoteStateTransition.cs
│   │   │   ├── QuoteMarketSchema.cs
│   │   │   └── RepeatOrderTemplate.cs
│   │   ├── Persistence/
│   │   │   ├── B2BDbContext.cs
│   │   │   ├── Configurations/                  # IEntityTypeConfiguration<T> per entity
│   │   │   └── Migrations/
│   │   ├── Messages/
│   │   │   ├── b2b.en.icu                       # ICU keys, EN
│   │   │   ├── b2b.ar.icu                       # ICU keys, AR (editorial-grade)
│   │   │   └── AR_EDITORIAL_REVIEW.md           # tracked AR keys pending editorial sign-off
│   │   └── Seeding/
│   │       ├── B2BReferenceDataSeeder.cs        # KSA + EG market schemas (Dev+Staging+Prod, idempotent)
│   │       └── B2BDevDataSeeder.cs              # synthetic companies + quotes spanning all states (Dev only, SeedGuard)
└── tests/
    └── B2B.Tests/
        ├── Unit/                                # state machine, business-day calc, reason-code mapper, market-policy resolution
        ├── Integration/                         # WebApplicationFactory + Testcontainers Postgres; every customer + approver + admin + company slice; concurrency guard; worker behavior with FakeTimeProvider
        └── Contract/                            # asserts every Acceptance Scenario from spec.md against live handlers
```

**Structure Decision**: Single vertical-slice module under the modular monolith named `B2B`, combining quote and company-account concerns because they ship together per the implementation plan and their state machines + entities are tightly coupled (a quote references a company; an approver acts on a quote; a company suspension affects every quote). Cross-module hooks (`IOrderFromQuoteHandler`, `IPricingBaselineProvider`, `ICartSnapshotProvider`) live under `Modules/Shared/` to avoid module dependency cycles (project-memory rule). The `Quotes/Customer/` ↔ `Quotes/Approver/` ↔ `Quotes/Admin/` sibling layout enforces visibly that the three actor-surfaces consume the same state machine but expose disjoint endpoints with disjoint RBAC.

## Implementation Phases

The `/speckit-tasks` run will expand each phase into dependency-ordered tasks. Listed here so reviewers can sanity-check ordering before tasks generation.

| Phase | Scope | Blockers cleared |
|---|---|---|
| A. Primitives | `QuoteState`, `QuoteStateMachine`, `CompanyInvitationState`, `CompanyInvitationStateMachine`, `QuoteReasonCode`, `BusinessDayCalculator`, `QuoteMarketPolicy` | Foundation for all slices |
| B. Persistence + migrations | 10 entities + EF configurations + initial migration; `B2BDbContext` with warning suppression; append-only trigger on `quote_state_transitions` | Unblocks all slices and workers |
| C. Reference seeder | `B2BReferenceDataSeeder` (KSA + EG market schemas, idempotent across all envs) | Unblocks integration tests + Staging/Prod boot |
| D. Cross-module hooks declared in `Modules/Shared/` | `IOrderFromQuoteHandler`, `IPricingBaselineProvider`, `ICartSnapshotProvider`, `QuoteDomainEvents`, `CompanyInvitationDomainEvents` | Unblocks Lane B start on UI; spec 011 / 007-a / 009 implement on their PRs |
| E. Company slices | RegisterCompany → GetMyCompany → UpdateCompanyConfig → AddBranch → RemoveBranch → InviteUser → AcceptInvitation → DeclineInvitation → RemoveMember → ChangeMemberRole → SuspendCompany | FR-019..FR-026 |
| F. Customer quote slices | RequestQuoteFromCart → RequestQuoteFromProduct → ListMyQuotes → GetMyQuote → WithdrawQuote → RequestRevision → SaveAsRepeatOrderTemplate → DownloadQuoteVersionDocument | FR-009..FR-013, FR-037 |
| G. Admin quote slices | ListQuoteQueue → GetQuoteDetail → AuthorQuoteDraft → PublishQuoteVersion (synchronous PDF render) | FR-014..FR-018 |
| H. Buyer + approver acceptance | SubmitAcceptance (buyer) → ListPendingApprovals (approver) → FinalizeAcceptance / RejectAcceptance (approver, with optimistic-concurrency guard) | FR-027..FR-031 |
| I. Conversion | `QuoteToOrderConverter` invokes `IOrderFromQuoteHandler` + `ICustomerVerificationEligibilityQuery` in same Tx as state transition; idempotency-key enforced | FR-032..FR-036 |
| J. Workers | `QuoteExpiryWorker` + `InvitationExpiryWorker`; advisory-lock guarded; `FakeTimeProvider`-friendly | FR-007, SC-006 |
| K. Account-lifecycle hook | `AccountLifecycleHandler` (subscribes to spec 020's `ICustomerAccountLifecycleSubscriber`) — voids in-flight quotes on locked/deleted/market-changed | FR-046, Edge Cases |
| L. Authorization wiring | `B2BPermissions.cs` constants + `[RequirePermission]` attributes; spec 015/019 wire role bindings on their PRs | Permission boundary |
| M. Domain events + 025 contract | Publish `QuoteRequested/Published/Accepted/Rejected/Expired/Withdrawn/PendingApprover/ApproverRejected` + `CompanyInvitation*` on each transition; spec 025 subscribes (lands on 025's PR, not here) | FR-043 |
| N. Contracts + OpenAPI | Regenerate `openapi.b2b.json`; assert contract test suite green; document every reason code | Guardrail #2 |
| O. AR/EN editorial | All customer-facing strings ICU-keyed; AR strings flagged in `AR_EDITORIAL_REVIEW.md`; PDF template AR text reviewed | P4 |
| P. Integration / DoD | Full Testcontainers run; concurrency-guard load test (US5 Scenario 2); time-driven worker tests; fingerprint; DoD checklist; audit-log spot-check script | PR gate |

## Complexity Tracking

> Constitution Check passed without violations. The rows below are *intentional non-obvious design choices* captured so future maintainers don't undo them accidentally.

| Design choice | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Single `B2B` module combining quotes + companies | Companies and quotes share entities (a quote references a company; an approver acts on a quote; a company suspension affects every quote) and ship together per the implementation plan task list. Splitting into `Quotes` + `Companies` modules forces an `ICompanyMembershipQuery` cross-module hook + a sync-test surface for membership changes. | Two separate modules double the DI seam without separating any domain boundary that meaningfully exists. |
| `QuoteVersion` as immutable rows (no UPDATE) | FR-003 + auditable price-revision history. Reviewers + buyers must see exactly what was published at each revision; mutating the row destroys the audit trail. | A single mutable `Quote` row with a denormalized "version_number" loses the per-version line-item snapshot when revisions happen. |
| `quote_version_documents` as separate rows per locale (one EN + one AR per version) | FR-018 wording is "one EN PDF + one AR PDF per version"; locale-specific signed-URL retrieval; per-locale regeneration possible. | Storing both locales in a single row complicates the storage-key + regeneration story; download endpoint needs a locale param either way. |
| `QuoteStateTransition` append-only with Postgres `BEFORE UPDATE OR DELETE` trigger | Spec 020 established this pattern; auditable history that cannot be silently rewritten. | A nullable "deleted_at" soft-delete column invites accidental rewrites and breaks the audit-replay assumption. |
| Synchronous PDF render on publish (rather than background queue) | V1 scale (≤500 quote-requests / day) does not justify a background-queue subsystem; QuestPDF + storage put fits inside the publish p95 budget; publish-failure-on-PDF-error is preferable to "publish succeeded but PDF will appear later" because spec 025 sends the customer notification *with the link* on publish. | A background queue would force the publish slice to optimistically respond before the PDF exists, which means either the customer notification has a missing-PDF window or the notification gets delayed. Both are worse UX than synchronous. |
| `xmin` optimistic concurrency for the multi-approver finalize race | SC-009: any-approver-finalize, first-action-wins; specs 020 + 008 already use this pattern. | Application-layer lock or Redis-based mutex introduces a dependency for a single race condition. |
| Below-baseline price overrides require non-empty reason captured in audit metadata | P10 + P25; finance must be able to replay overrides for margin-impact analysis. SC-004 requires it. | Overrides without reasons are unauditable; this is a constitutional non-negotiable, not a nice-to-have. |
| `unique_po_required` enforced via Postgres unique partial index `(company_id, po_number) WHERE po_number IS NOT NULL` | Clarification §Q3 locked: PO uniqueness scope is "all quotes ever for that company". A unique partial index is the deterministic enforcement; application-layer validation is racy under concurrent acceptance. | Application-layer "is this PO unique?" check fails under concurrency; deduplication via post-hoc audit cleanup is messy. |
| Quote cool-down / re-quote rules **not** introduced (no equivalent to spec 020's rejected cool-down) | Quotes are commercial proposals, not eligibility decisions; there's no defensible reason to lock customers out of re-requesting. The customer can request a new quote immediately after rejection. | Adding a cool-down would punish legitimate customers who legitimately need a re-quote with a different SKU mix. |
| `vendor_id` slot reserved (jsonb in `restriction_policy_snapshot`) but never populated in V1 | P6 multi-vendor-readiness without paying schema-migration cost in Phase 2. Same pattern as spec 020. | Omitting forces a migration of `quotes` + `quote_versions` + the conversion contract when vendor-scoped quoting lands. |
| Two state machines (Quote + CompanyInvitation), not one | Quote and CompanyInvitation lifecycles are independent; merging them would force a polymorphic state column that hides transition guards. | A single `B2BState` enum collapses two domains and weakens compile-time guarantees. |
| `BusinessDayCalculator` duplicated from spec 020 (not extracted to `Modules/Shared/`) | Trivial pure function (~30 lines); cross-module shared code introduces a coupling for de minimis savings. Spec 020 + 021 each test their own copy. | Extracting to `Modules/Shared/` adds a versioning concern (when does spec 020's calc need to differ from 021's?) for ≈ 30 LOC. |
