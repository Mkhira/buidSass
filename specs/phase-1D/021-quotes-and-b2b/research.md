# Phase 0 — Research: Quotes and B2B (Spec 021)

**Date**: 2026-04-28
**Spec**: [spec.md](./spec.md)
**Plan**: [plan.md](./plan.md)

This document closes implementation-research questions left open by the plan's Technical Context. The five spec-level clarifications recorded in `spec.md §Clarifications (Session 2026-04-28)` are not repeated; the items below are the next-layer-down decisions that surfaced while authoring the plan.

---

## R1. Multi-approver finalize race — concurrency strategy

**Decision**: Use EF Core optimistic concurrency via Postgres `xmin` system column mapped as `IsRowVersion()` on `quotes`. Both `FinalizeAcceptanceHandler` and `RejectAcceptanceHandler` load the current `Quote` row, mutate, and `SaveChangesAsync()`. The loser receives `DbUpdateConcurrencyException`, which the handler maps to `409 Conflict` with reason code `quote.already_decided`. SC-009 is asserted by an integration test that fires 100 parallel finalize calls from two approvers simultaneously and verifies exactly one commit, no double audit event, no double order created.

**Rationale**:
- `xmin` is a zero-storage, zero-trigger optimistic-concurrency token native to Postgres; specs 008 + 020 already use this pattern for stock writes and decision concurrency.
- "Any approver finalizes" (clarification §Q1) means we cannot bind quotes to a single approver — every approver of the company can act on every `pending-approver` quote — so a per-approver lock or per-quote pessimistic lock would serialize all approver activity unnecessarily.
- Idempotency (`Idempotency-Key` on the finalize endpoint) is a separate, complementary layer per spec 003: idempotency replays the original 200; concurrency rejects a competing decision.

**Alternatives considered**:
- **Pessimistic row lock (`SELECT ... FOR UPDATE`)**: serializes approver activity; works but forces approvers to wait when both open the same queue list; harder to test deterministically.
- **Application-layer mutex / Redis lock**: introduces Redis dependency for one race condition.
- **Postgres advisory locks**: used by workers (R7); not appropriate for request-scoped coordination because the lock is bound to the connection rather than the transaction.

---

## R2. Business-day calculator — duplicate from spec 020 vs extract to `Modules/Shared/`

**Decision**: Duplicate `BusinessDayCalculator.cs` into `Modules/B2B/Primitives/`. Pure function (~30 LOC); reads the market policy's `holidays_list` (jsonb) at call sites; identical implementation to spec 020's copy.

**Rationale**:
- Premature extraction to `Modules/Shared/` introduces a versioning concern: when does spec 020's calc need to differ from 021's? Likely never, but the moment it does, a shared file forces a coordinated PR across two modules.
- The function is trivial (no DI, no IO, no side effects); maintenance cost of two copies is bounded; tests live in each module's test project.
- Project memory: cross-module hooks via `Modules/Shared/` to avoid cycles. The opposite — pure duplicated utility code — is a recommended pattern when extraction would be premature optimization.

**Alternatives considered**:
- **Extract to `Modules/Shared/BusinessDayCalculator.cs`**: trades a small coupling for a small DRY win; rejected because the coupling is real and the DRY win is on ~30 LOC.

---

## R3. Quote PDF rendering — synchronous on publish vs background queue

**Decision**: Synchronous render on `PublishQuoteVersion` for V1. The slice generates one EN PDF + one AR PDF via the existing `Modules/Pdf/IPdfService` (QuestPDF-based), persists each via `IStorageService.UploadAsync`, then INSERTs `quote_version_documents` rows — all inside the same Tx as the `QuoteVersion` insert and the state transition. p95 budget ≤ 3 s; if breached on a specific theme, the slice can be moved to a `Channel<>`-backed in-process background queue without contract change.

**Rationale**:
- V1 scale (≤500 quote requests / day across both markets, peak ≤50 concurrent admin authoring sessions) does not justify introducing a background-queue subsystem.
- Customer notification (sent via spec 025 on `QuotePublished`) carries a download link; if the PDF doesn't exist at notification dispatch, the link 404s — an unrecoverable UX failure. Synchronous publish-or-fail keeps the publish atomic.
- QuestPDF is already vendored and used by spec 012's tax-invoice rendering; reusing it is the path of least friction.

**Alternatives considered**:
- **Background queue (Channel<> in-process)**: needed at higher scale; adds "publish succeeded but PDF will appear later" UX wrinkle.
- **External rendering service** (Headless Chrome, etc.): out of scope; QuestPDF covers the layout requirements (RTL Arabic, currency formatting, line items, totals).
- **PDF on first download** (lazy): customers expect the link in the notification to work immediately; lazy generation creates a confusing first-click delay.

---

## R4. Cart snapshot at quote-request — cross-module hook design

**Decision**: Declare `ICartSnapshotProvider` in `Modules/Shared/`, implemented by spec 009 (`cart`). Single method:

```csharp
public interface ICartSnapshotProvider
{
    /// <summary>
    /// Atomically snapshots the customer's current cart contents and clears the cart.
    /// Returns the snapshot for the caller to persist into the quote.
    /// If the cart is empty, returns an empty snapshot (no error — the caller validates).
    /// </summary>
    ValueTask<CartSnapshot> SnapshotAndClearAsync(Guid customerId, CancellationToken ct);
}

public sealed record CartSnapshot(
    IReadOnlyList<CartSnapshotLine> Lines,
    DateTimeOffset SnapshottedAt);

public sealed record CartSnapshotLine(
    string Sku,
    int Quantity,
    string? LineNote);
```

Spec 009 owns the implementation; this spec owns the declaration. Atomicity: the snapshot read + cart clear happen inside the cart's own Tx, not the quote's — the quote's request Tx persists the returned snapshot into the `quotes.originating_cart_snapshot` jsonb column.

**Rationale**:
- FR-010: subsequent cart edits MUST NOT modify the quote → the cart must be cleared atomically with the snapshot, otherwise a race lets the buyer edit the cart between snapshot and clear.
- Project-memory rule: cross-module hooks via `Modules/Shared/` to avoid module dependency cycles.
- Two separate Tx (cart vs quote) is acceptable because the quote rolls back if persistence fails — leaving the cart cleared. We accept this minor failure mode (buyer's cart is empty after a failed quote-request) rather than introducing a distributed transaction. The integration test asserts the failure is recoverable and the buyer-facing message is localized.

**Alternatives considered**:
- **Quote owns its own line items end-to-end (no cart consumption)**: forces the customer to re-enter SKUs in a quote-specific UI, which is a hostile UX for B2B procurement workflows.
- **Cart stays alive after snapshot**: violates FR-010's "subsequent cart edits MUST NOT modify the quote".

---

## R5. Pricing baseline + tax preview — cross-module hook design

**Decision**: Declare `IPricingBaselineProvider` in `Modules/Shared/`, implemented by spec 007-a (`pricing-and-tax-engine`). Single method:

```csharp
public interface IPricingBaselineProvider
{
    /// <summary>
    /// Returns baseline price + applicable promotions + tax preview for the requested SKUs,
    /// scoped to the buying customer (for tier/business-pricing) and the customer's market.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, PricingBaseline>> GetBaselinesAsync(
        Guid customerId,
        IReadOnlyCollection<string> skus,
        CancellationToken ct);
}

public sealed record PricingBaseline(
    string Sku,
    decimal BaselineUnitPrice,
    string Currency,                          // e.g. "SAR" or "EGP"
    IReadOnlyList<AppliedPromotion> Promotions,
    decimal TaxPreviewUnitAmount,
    decimal TaxPreviewRatePct);

public sealed record AppliedPromotion(
    string PromotionCode,
    string LocalizedDescriptionKey,
    decimal LineDiscountAmount);
```

Bulk by design — admin authoring loads N SKUs at once.

**Rationale**:
- P10 (Pricing centralized): the admin authoring slice MUST consume 007-a's pricing logic, not reimplement it.
- Bulk method matches typical authoring usage (up to ~50 lines per quote).
- Tax preview is informational only — the order's tax is computed authoritatively at conversion time by spec 011 / 007-a; the quote captures the *preview* on each `QuoteVersion` so the customer's PDF shows what they were quoted.
- Tax-preview drift threshold (per-market, default 5%) gates the conversion: if `(quote.tax_preview - order.tax_authoritative) / quote.tax_preview > threshold`, the conversion surfaces a confirm prompt (US6 Edge Case "Tax preview drift").

**Alternatives considered**:
- **Inline pricing-engine call from each authoring slice**: reimplements 007-a's logic; violates P10.
- **Quote captures only baseline, no tax preview**: the customer's PDF can't show tax — unacceptable for B2B procurement (the buyer needs gross totals for budget approval).

---

## R6. Quote-to-order conversion — atomicity and idempotency

**Decision**: Declare `IOrderFromQuoteHandler` in `Modules/Shared/`, implemented by spec 011 (`orders`). Single method:

```csharp
public interface IOrderFromQuoteHandler
{
    /// <summary>
    /// Creates an order from an accepted quote. Runs inside the caller's transaction.
    /// Idempotent on (quoteId, idempotencyKey) — replays return the existing orderId.
    /// </summary>
    ValueTask<OrderConversionResult> CreateAsync(
        QuoteConversionRequest request,
        CancellationToken ct);
}

public sealed record QuoteConversionRequest(
    Guid QuoteId,
    Guid CustomerId,
    Guid? CompanyId,
    Guid? CompanyBranchId,
    string MarketCode,
    string? PoNumber,
    bool InvoiceBilling,
    int? TermsDays,
    IReadOnlyList<QuoteConversionLine> Lines,
    decimal Subtotal,
    decimal TotalDiscount,
    decimal GrandTotal,
    Guid IdempotencyKey);

public sealed record QuoteConversionLine(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineDiscount,
    decimal LineTaxPreview);

public sealed record OrderConversionResult(
    Guid OrderId,
    bool WasIdempotentReplay);
```

The B2B module's `QuoteToOrderConverter`:
1. Opens a Tx on `B2BDbContext`.
2. Calls `ICustomerVerificationEligibilityQuery` (spec 020) for every restricted SKU on the quote (FR-036). Failure → `quote.eligibility_required` rejection; quote stays in prior state.
3. Calls `IOrderFromQuoteHandler.CreateAsync` (spec 011). Spec 011 enrolls in the same `IDbContextTransaction` (or coordinates via the platform's outbox if Tx-sharing is unavailable in the modular monolith — see alternatives).
4. If 2 + 3 succeed, transitions the quote to `accepted` and writes the audit event.
5. Commits.

**Rationale**:
- SC-007 demands atomicity: across 100 simulated conversions where order-creation deliberately fails 30%, the quote ends up in its prior state for every failure.
- Idempotency-Key is the request identity; `OrderConversionResult.WasIdempotentReplay` lets the converter detect a replay and skip the state-transition work cleanly.
- Eligibility check inside the same Tx so a verification expiry that races the conversion is observed atomically.

**Alternatives considered**:
- **Two-phase commit / saga**: heavyweight; not justified at modular-monolith scale.
- **Order created first, then quote-state changed in a follow-up event**: violates SC-007 atomicity.
- **Quote-state changed first, then order created**: leaves the quote in `accepted` with no order if the order creation fails — strictly worse than the recommended order.

---

## R7. Worker scheduling + horizontal coordination

**Decision**: Two `IHostedService` workers, both using `BackgroundService` + `PeriodicTimer(TimeSpan.FromHours(24))`. Default UTC start times (configurable via `appsettings.json`, locked into Key Vault for Staging/Prod):
- `QuoteExpiryWorker` — 03:15 UTC.
- `InvitationExpiryWorker` — 03:45 UTC.

(Note: these slot between spec 020's expiry/reminder/purge workers at 03:00/03:30/04:00 UTC.)

Each worker takes a Postgres advisory lock (`pg_try_advisory_lock(<hash>)`) before running so that horizontal-scaled instances don't double-execute. `FakeTimeProvider` injection for tests.

**Rationale**:
- Daily cadence matches FR-007 ("scheduled job moves quotes past `expires_at`") and matches the 14-day invitation TTL (no need for sub-daily resolution).
- Slotting between spec 020's workers avoids contention on the database during the 03:00–04:00 UTC window.
- Advisory locks are the lightest horizontal-scale guard in the .NET-on-Postgres stack and match the pattern adopted by spec 020.

**Alternatives considered**:
- **Hourly cadence**: 24× more wakeups for daily-meaningful effect; rejected.
- **Quartz.NET / Hangfire**: full job scheduler for two jobs; not justified.

---

## R8. Reason-code enum surface

**Decision**: `QuoteReasonCode` (enum) lives in `Modules/B2B/Primitives/`. Each enum value maps to (a) an ICU key in `b2b.en.icu` and `b2b.ar.icu`, (b) a documented entry in `contracts/quotes-and-b2b-contract.md` with the structural shape. Inventory (V1):

`quote.required_field_missing`, `quote.cart_empty`, `quote.product_not_quotable`, `quote.no_active_company_membership`, `quote.po_required`, `quote.po_already_used` (hard reject when `unique_po_required=true`), `quote.po_warning_acknowledged` (soft warn when `unique_po_required=false`), `quote.rate_limit_exceeded`, `quote.market_mismatch`, `quote.eligibility_required`, `quote.invalid_state_for_action`, `quote.no_changes_provided`, `quote.no_approver_available`, `quote.cooldown_active` (deliberately reserved but unused in V1 — see R10), `quote.already_decided` (optimistic concurrency), `quote.reason_required`, `quote.below_baseline_reason_required`, `quote.expired`, `quote.tax_preview_drift_threshold_exceeded`, `quote.idempotency_replay`, `quote.account_inactive`, `quote.company_suspended`, `quote.product_archived`, `company.tax_id_invalid`, `company.duplicate_tax_id`, `company.last_admin_cannot_be_removed`, `company.last_approver_cannot_be_removed_with_required`, `company.member_already_exists`, `company.invitation_email_invalid`, `company.invitation_already_pending`, `company.invitation_expired`, `template.name_already_exists`.

**Rationale**:
- Spec 014 (Flutter customer app) and spec 015 (admin web) consume reason codes to render localized messages and pick CTAs. Stable codes are the contract; stable codes don't move when ICU strings change.
- One central enum + one central ICU bundle = one PR to add a code. No proliferation risk.

**Alternatives considered**:
- **Per-slice enums**: fragments the contract; consumers can't write a single switch.

---

## R9. Authorization wiring across modules

**Decision**: Permissions declared by this spec:
- `quotes.author` — admin authoring + revisions + publish.
- `quotes.review` — read-only across every quote (admin support / finance scenarios).
- `companies.admin` — used by spec 019 admin-customers' company-moderation surface (exists here as a constant; granted by spec 019's role model). NOT the same as the customer-side `companies.admin` membership role; clearly disambiguated as `quotes-admin.companies.admin` in code if needed.
- `companies.suspend` — admin-side action on a company; granted by spec 019's role model.

Customer-side roles (`buyer`, `approver`, `companies.admin` membership role, individual customer) are derived from `CompanyMembership` rows + identity claims, NOT from the platform RBAC system. The customer-surface authorization is membership-driven.

**Rationale**:
- Spec 020 established the pattern: permissions owned by the enforcing module; roles owned by the composing module.
- Customer-side membership is a domain concept, not an admin RBAC concept; mixing them would force every membership change through the admin RBAC infrastructure, which is overkill.

**Alternatives considered**:
- **All authorization through admin RBAC**: forces membership changes through admin tooling; violates the user-administered company-account model.

---

## R10. Quote rejection / re-quote — cool-down vs no cool-down

**Decision**: **No cool-down** after a rejected quote. The customer can request a new quote immediately. The `QuoteReasonCode.cooldown_active` constant is reserved in the enum (R8) for forward compatibility but never raised in V1.

**Rationale**:
- Spec 020's verification cool-down exists because verification rejection is a regulator-correctness decision; re-spamming the queue with the same submission is operational noise.
- A quote rejection is a commercial proposal that wasn't accepted — a different SKU mix, different terms, or different timing genuinely justifies a new quote. Locking out legitimate buyers is hostile UX.
- The rate-limit caps (FR-045) protect against spam abuse independent of cool-down.

**Alternatives considered**:
- **Cool-down (e.g. 24 h)**: rejected for the reasons above.
- **Per-customer-rate-limit only**: kept as the existing FR-045 protection.

---

## R11. Tax-preview drift threshold and surface behavior

**Decision**: `tax_preview_drift_threshold_pct` lives in `quote_market_schemas` with default `5.0%` for both KSA and EG. At conversion time, if `abs(order.tax_authoritative - quote.tax_preview) / quote.tax_preview > threshold`, the converter does NOT auto-finalize; it returns `quote.tax_preview_drift_threshold_exceeded` to the caller along with the new tax amount. The buyer (US2) or approver (US1) is shown the drift in their preferred locale and must explicitly confirm to proceed; the confirmation is captured as a transition metadata field.

**Rationale**:
- Edge case "Tax preview drift": the order's tax is authoritative; the quote's tax is informational. A material drift between the two (a tax rate change between authoring and acceptance, for example) needs human confirmation.
- 5% is wide enough to ignore VAT-rounding noise, narrow enough to catch a rate change.
- The threshold being per-market via `quote_market_schemas` matches the P5 pattern.

**Alternatives considered**:
- **Auto-accept any drift** (the order's tax silently overrides the quote's preview): violates the buyer's "I approved a quote with this gross total" expectation.
- **Hard-reject any drift**: the buyer would have to wait for an admin re-author for any 0.01% variation.
- **Threshold in code, not config**: violates P5.

---

## R12. Repeat-order template uniqueness scope

**Decision**: Uniqueness on `(company_id, name)` for company-owned templates and `(user_id, name)` for individual-customer templates, enforced via two unique partial indexes:

```sql
CREATE UNIQUE INDEX ix_repeat_order_templates_company_name
  ON repeat_order_templates (company_id, name)
  WHERE company_id IS NOT NULL;

CREATE UNIQUE INDEX ix_repeat_order_templates_user_name
  ON repeat_order_templates (user_id, name)
  WHERE company_id IS NULL;
```

**Rationale**:
- US7 Acceptance Scenario 2 explicitly forbids duplicate names "within company-account".
- Two partial indexes (one for company-owned, one for individual-owned) allow templates to be scoped correctly without forcing a fake "personal company" entity.
- Unique partial indexes are deterministic enforcement; application-layer checks are racy under concurrent saves.

**Alternatives considered**:
- **Single uniqueness on `(coalesce(company_id, user_id), name)`** with a generated column: adds a generated column for one constraint; net complexity higher than two partial indexes.

---

## R13. Account-lifecycle hook — voiding quotes on locked / deleted / market-changed

**Decision**: Implement `AccountLifecycleHandler : ICustomerAccountLifecycleSubscriber` (subscribing to spec 020's existing interface). Behavior:
- `CustomerAccountLocked` → void all non-terminal quotes owned by the customer (state → `withdrawn` with reason `account_inactive`); active company memberships unaffected.
- `CustomerAccountDeleted` → void all non-terminal quotes (reason `account_deleted`); cascade-delete `CompanyMembership` rows where the customer is the *only* member (not the only admin — that's a separate FR-024 flow at admin-action time, but at delete-time we accept that the company becomes adminless if the customer was the last admin; spec 019 is responsible for orphaned-company cleanup).
- `CustomerMarketChanged` → void all non-terminal quotes (reason `customer_market_changed`); `accepted` quotes are not voided (the order already exists).

**Rationale**:
- FR-046 + FR-026 require these behaviors for security + correctness.
- Reusing spec 020's existing subscriber surface avoids inventing a new event channel.
- The "delete leaves an adminless company" is acceptable in V1 because spec 019 (admin-customers) ships the moderation surface that cleans up orphaned companies; pre-empting that here would couple the modules.

**Alternatives considered**:
- **Lazily check account state on every quote read**: defers the work; cache rows go stale and security posture weakens.

---

## R14. EF Core warning suppression and DI scope (project-memory rule)

**Decision**: `B2BModule.cs`'s `AddDbContext<B2BDbContext>` registration suppresses `RelationalEventId.ManyServiceProvidersCreatedWarning` (per the project-memory rule). Scope is `Scoped` (default). The conversion handler explicitly opens a transaction on `B2BDbContext`; spec 011's `IOrderFromQuoteHandler` is invoked inside that transaction's scope (the platform exposes a shared `IDbContextTransactionCoordinator` for cross-module Tx — verified in spec 003's pattern; if absent, the fallback is "spec 011 enrolls in the ambient connection and shares the Tx").

**Rationale**: Documented project-wide pattern. Skipping suppression breaks Identity test isolation.

---

## R15. OpenAPI artifact convention

**Decision**: Generate `services/backend_api/openapi.b2b.json` at the same root as the existing per-module artifacts (`openapi.identity.json`, `openapi.catalog.json`, etc.). Reviewers diff this file in PRs to satisfy Guardrail #2.

---

## Open items deferred (with justification)

| Item | Why deferred | Where it lands |
|---|---|---|
| Repeat-order template management UI (list, edit, delete, schedule, recurrence) | FR-038: backend stubs only in V1; full UI is spec 1.5-c. | Phase 1.5-c (`b2b-reorder-templates`). |
| Quote-PDF retention purge | Quotes don't share spec 020's "purge after retention window" model — accepted/rejected/expired/withdrawn quotes are preserved indefinitely for finance + audit replay. PDFs are purged only when the parent quote is voided (FR-046 path), which is rare. | Spec 028 (`analytics-audit-monitoring`) when retention reporting lands. |
| Multi-vendor quote splitting | FR-044 reserves the `vendor_id` slot; populating it requires a cross-vendor accept-and-split UX that doesn't exist in V1. | Phase 2-d (`split-checkout`). |
| External CRM / ERP sync | Out of scope for V1; the audit log + back-link are the system of record. | Phase 1.5+ extension PR. |
| Quote PDF accessibility (screen-reader semantics inside PDF) | QuestPDF supports tagged PDFs but the level needed for accessibility audits varies; deferring to a focused accessibility pass. | Phase 1F (`qa-and-hardening`) or Phase 1.5. |
