# Quickstart — Implementer Walkthrough: Quotes and B2B (Spec 021)

**Date**: 2026-04-28
**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/quotes-and-b2b-contract.md](./contracts/quotes-and-b2b-contract.md)

This is the path a Lane A engineer (or a Codex/Claude agent) follows to land the first vertical slice of spec 021. It is intentionally minimal — `tasks.md` (produced by `/speckit-tasks`) is the canonical task-by-task plan; this file is the smoke-test loop that proves the module is wired correctly before slice work begins.

---

## 0. Prerequisites

- Specs 004 (Identity), 005 (Catalog), 007-a (Pricing), 009 (Cart), 010 (Checkout), 011 (Orders), 012 (Tax invoices), 020 (Verification) at DoD on `main`.
- Spec 015 (admin-foundation) **contract** merged on `main`. The admin web shell does not need to be built before this spec's backend lands — only the contract.
- `Modules/Pdf/IPdfService` available (already on `main` per spec 003 / 012).
- `Modules/Storage/IStorageService` + `Modules/AuditLog/IAuditEventPublisher` available.
- Local Postgres + `dotnet --version` ≥ 9.x.

---

## 1. Module skeleton (Phases A + B)

Create the layout per [plan.md §Project Structure](./plan.md). The minimum to build green:

```
services/backend_api/Modules/B2B/
  B2BModule.cs
  Primitives/
    QuoteState.cs
    QuoteStateMachine.cs
    QuoteActorKind.cs
    QuoteReasonCode.cs
    CompanyInvitationState.cs
    CompanyInvitationStateMachine.cs
    QuoteMarketPolicy.cs
    BusinessDayCalculator.cs
  Entities/                       # 10 entities per data-model
  Persistence/
    B2BDbContext.cs
    Configurations/                # one IEntityTypeConfiguration per entity
    Migrations/
  Authorization/
    B2BPermissions.cs
```

`B2BModule.cs` MUST suppress `RelationalEventId.ManyServiceProvidersCreatedWarning` on `AddDbContext`:

```csharp
services.AddDbContext<B2BDbContext>((sp, opts) =>
{
    opts.UseNpgsql(connectionString);
    opts.ConfigureWarnings(w =>
        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.ManyServiceProvidersCreatedWarning));
});
```

Run the migration:

```bash
dotnet ef migrations add B2BInit \
  --project services/backend_api \
  --context B2BDbContext \
  --output-dir Modules/B2B/Persistence/Migrations
dotnet ef database update --context B2BDbContext --project services/backend_api
```

**Smoke check**: `dotnet build services/backend_api` is green; the migration creates 10 tables and the `quote_state_transitions` append-only trigger.

---

## 2. Reference seeder (Phase C)

`Modules/B2B/Seeding/B2BReferenceDataSeeder.cs` implements the platform `ISeeder` interface (idempotent) and inserts two `quote_market_schemas` rows:

```text
KSA: version=1, validity_days=14, rate_limit_per_customer_per_hour=10,
     rate_limit_per_company_per_hour=50, company_verification_required=false,
     tax_preview_drift_threshold_pct=5.00, sla_decision_business_days=2,
     sla_warning_business_days=1, invitation_ttl_days=14
EG:  version=1, same defaults
```

Run via the platform seed CLI:

```bash
dotnet run --project services/backend_api -- seed --mode=apply --tag=b2b-reference
```

**Smoke check**: `SELECT count(*) FROM quote_market_schemas;` returns 2.

---

## 3. Cross-module hooks (Phase D)

Create the four `Modules/Shared/` files:

```
Modules/Shared/
  IOrderFromQuoteHandler.cs            # contract spec 011 will implement
  IPricingBaselineProvider.cs          # contract spec 007-a will implement
  ICartSnapshotProvider.cs             # contract spec 009 will implement
  QuoteDomainEvents.cs                 # subscribed by spec 025
  CompanyInvitationDomainEvents.cs     # subscribed by spec 025
```

Until spec 011 / 007-a / 009 update their PRs, register stub implementations in test fixtures only:
- `StubOrderFromQuoteHandler` — returns a deterministic OrderId.
- `StubPricingBaselineProvider` — returns SKU baseline = 100.00 + flat 15% tax preview.
- `StubCartSnapshotProvider` — returns a 2-line snapshot.

These stubs live under `tests/B2B.Tests/Fixtures/`, never in production DI.

---

## 4. First customer slice — `RegisterCompany` (Phase E)

The simplest end-to-end vertical slice that proves the module's plumbing. (Quote slices are deeper because they need cart + pricing seams; companies are self-contained.)

**Slice files**:
```
Modules/B2B/Companies/RegisterCompany/
  RegisterCompanyRequest.cs            # name {en, ar}, tax_id, market_code, primary_address, billing_address?, flags
  RegisterCompanyValidator.cs
  RegisterCompanyHandler.cs            # MediatR; transactional
  RegisterCompanyEndpoint.cs           # POST /api/customer/companies
```

**Handler outline** (read [data-model.md §3.1](./data-model.md) for invariants):

```csharp
public sealed class RegisterCompanyHandler : IRequestHandler<RegisterCompanyRequest, Guid>
{
    public async Task<Guid> Handle(RegisterCompanyRequest req, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // 1. Validate market matches caller's market-of-record.
        // 2. Resolve active QuoteMarketSchema for the market.
        // 3. Insert Company row (state = 'active' if schema.company_verification_required=false, else 'pending-verification').
        // 4. Insert two CompanyMembership rows: caller as 'companies.admin' AND 'buyer'.
        // 5. Audit: company.config_changed (initial) + company.member_changed (×2).
        // 6. tx.CommitAsync.
        // 7. Return Company.id.
    }
}
```

**Smoke test** (manual, against local API, with a test customer JWT in `$JWT`):

```bash
curl -X POST http://localhost:5050/api/customer/companies \
  -H "Authorization: Bearer $JWT" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -H "Accept-Language: ar" \
  -d '{
        "name": { "en": "ABC Dental Clinic LLC", "ar": "شركة أ.ب.ج لطب الأسنان" },
        "tax_id": "300000000000003",
        "market_code": "ksa",
        "primary_address": { "line1": "Riyadh", "city": "Riyadh", "country": "SA" }
      }'
# Expected: 201 with the new company id
```

Then:

```sql
SELECT state, market_code FROM companies WHERE tax_id = '300000000000003';
-- Expect: ('active', 'ksa')

SELECT count(*) FROM company_memberships WHERE company_id = '<new_id>';
-- Expect: 2 (the caller is companies.admin AND buyer)

SELECT count(*) FROM audit_log_entries
WHERE event_kind IN ('company.config_changed', 'company.member_changed');
-- Expect: ≥ 3 (one config_changed + two member_changed)
```

If all queries match, plumbing is wired.

---

## 5. First quote slice — `RequestQuoteFromCart` (Phase F)

After the company slice lands, the cart-quote path is the integration smoke for the `ICartSnapshotProvider` cross-module hook + `IPricingBaselineProvider` consumption.

The handler:
1. Calls `_cartSnapshotProvider.SnapshotAndClearAsync(customerId)` (spec 009 owns the implementation; tests use the stub).
2. If snapshot empty → reject with `quote.cart_empty`.
3. Calls `_productRestrictionPolicy.GetForSkuAsync` per line → snapshots into `quotes.restriction_policy_snapshot`.
4. Calls `_pricingBaselineProvider.GetBaselinesAsync` for the line SKUs → captures the pricing baseline as a transient (NOT persisted on the quote at request time — admin authoring will re-query).
5. Inserts `Quote` (state=`requested`) + `QuoteStateTransition` (`__none__ → requested`).
6. Publishes `QuoteRequested` (in-process bus → consumed by spec 025).
7. Audit `quote.state_changed`.

**Smoke test**:

```bash
# Assume the test cart provider returns 2 lines for $JWT's customer.
curl -X POST http://localhost:5050/api/customer/quotes/from-cart \
  -H "Authorization: Bearer $JWT" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{ "company_id": "<companyId>", "po_number": "PO-2026-0042", "message": { "en": "Bulk pricing please." } }'
# Expected: 201 with quote.state == "requested"
```

Then:

```sql
SELECT state, company_id, po_number FROM quotes WHERE customer_id = '<test-customer>';
-- Expect: ('requested', '<companyId>', 'PO-2026-0042')

SELECT prior_state, new_state, actor_kind FROM quote_state_transitions
WHERE quote_id = '<the-new-id>';
-- Expect: ('__none__', 'requested', 'buyer')
```

---

## 6. Admin authoring + publishing — first PDF render (Phase G)

After Phase F lands, the admin slice exercises the QuestPDF path:

```bash
# As an admin user with quotes.author permission:
curl -X POST http://localhost:5050/api/admin/quotes/<quoteId>/draft \
  -H "Authorization: Bearer $ADMIN_JWT" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{
        "lines": [
          { "sku": "ABC-123", "quantity": 5, "override_unit_price": 119.00, "override_reason": { "en": "Bulk." }, "line_discount_amount": 0 }
        ],
        "terms_text": { "en": "Net 30", "ar": "صافي 30 يومًا" },
        "terms_days": 30,
        "validity_extends": false,
        "internal_note": ""
      }'
# Then publish:
curl -X POST http://localhost:5050/api/admin/quotes/<quoteId>/publish \
  -H "Authorization: Bearer $ADMIN_JWT" \
  -H "Idempotency-Key: $(uuidgen)"
```

```sql
SELECT version_number, validity_extends FROM quote_versions WHERE quote_id = '<quoteId>';
-- Expect: 1 row with version_number = 1

SELECT count(*) FROM quote_version_documents WHERE quote_version_id = '<versionId>';
-- Expect: 2 (one EN, one AR)

SELECT state, current_version_id, expires_at FROM quotes WHERE id = '<quoteId>';
-- Expect: state='revised', expires_at = published_at + market.validity_days
```

If the two PDFs landed in storage (verify via the storage abstraction's CLI), Phase G is functional.

---

## 7. Acceptance + conversion smoke (Phases H + I)

The buyer-acceptance path with `approver_required=true` and one approver:

1. Buyer calls `POST /quotes/{id}/submit-acceptance` → state moves to `pending-approver`.
2. Approver calls `POST /quotes/{id}/finalize-acceptance` → state moves to `accepted`; `IOrderFromQuoteHandler` is called; an order id is returned.

```sql
SELECT state, decided_at FROM quotes WHERE id = '<quoteId>';
-- Expect: 'accepted', decided_at = recent.

SELECT order_id FROM orders WHERE source_quote_id = '<quoteId>';
-- Expect: 1 row.
```

For the concurrency test (SC-009), use `Parallel.ForEachAsync` in an integration test to fire 100 simultaneous finalize calls from two different approvers; assert exactly one commit, one audit event, one order. The 99 losers receive `409 quote.already_decided`.

---

## 8. Workers (Phase J)

`QuoteExpiryWorker` and `InvitationExpiryWorker` are `IHostedService`s with daily `PeriodicTimer`. For local dev, override the period via `appsettings.Development.json`:

```json
{
  "B2B": {
    "Workers": {
      "Expiry":     { "Period": "00:01:00", "StartUtc": "00:00:00" },
      "Invitation": { "Period": "00:01:00", "StartUtc": "00:00:00" }
    }
  }
}
```

`FakeTimeProvider` is the test injection point.

**Smoke check** for `QuoteExpiryWorker`:
1. Use `B2BDevDataSeeder` to insert one `revised` quote with `expires_at = now() - interval '1 hour'`.
2. Wait for the 1-minute dev tick.
3. Assert `state = 'expired'`, `terminal_at` set, `quote.state_changed` audit event written, `QuoteExpired` domain event observed.

---

## 9. Tests checklist (per spec — `tests/B2B.Tests/`)

| Test | Phase | Purpose |
|---|---|---|
| `Unit/QuoteStateMachineTests` | A | Forbid terminal → non-terminal; allow defined transitions; reject unknown transitions. |
| `Unit/CompanyInvitationStateMachineTests` | A | Same shape for the invitation state machine. |
| `Unit/BusinessDayCalculatorTests` | A | Sun–Thu working week; respects holidays; SLA arithmetic deterministic. |
| `Unit/QuoteReasonCodeIcuKeysTests` | A | Every enum value has an ICU key in EN + AR. |
| `Integration/RegisterCompanyTests` | E | Happy path + every error reason in §5.1 + invariants (caller is admin + buyer; both audited). |
| `Integration/CompanyMembershipInvariantsTests` | E | Last-admin-cannot-be-removed; last-approver-cannot-be-removed-with-required. |
| `Integration/InvitationLifecycleTests` | E + J | Send → accept; send → expire (worker); send → decline; uniqueness on `(company, email, role)`. |
| `Integration/RequestQuoteFromCartTests` | F | Happy path + every error in §2.1 + cart cleared atomically. |
| `Integration/RequestQuoteFromProductTests` | F | Happy path + cart NOT cleared. |
| `Integration/AdminAuthoringTests` | G | Below-baseline override requires reason; reason audited; first publish moves to `revised` and generates two PDFs. |
| `Integration/PublishGeneratesPdfsTests` | G | Asserts two `quote_version_documents` rows + storage blob round-trip. |
| `Integration/AcceptanceWithApproverRequiredTests` | H | Buyer submits → state `pending-approver`; approver finalizes → `accepted` + order created. |
| `Integration/AcceptanceWithoutApproverTests` | H | Direct buyer acceptance when `approver_required=false` or no approver designated. |
| `Integration/MultiApproverConcurrencyTests` | H | 100 parallel finalize calls; exactly one wins; SC-009. |
| `Integration/ConversionAtomicityTests` | I | 30% spec-011-failure rate; quote stays in prior state; no half-state. |
| `Integration/EligibilityAtAcceptanceTests` | I | Expired verification on a restricted-SKU line → `quote.eligibility_required` (FR-036). |
| `Integration/QuoteExpiryWorkerTests` | J | `FakeTimeProvider`-driven; expiry transition + audit + cache flip. |
| `Integration/InvitationExpiryWorkerTests` | J | TTL elapsed → `expired`; idempotent on re-run. |
| `Integration/AccountLifecycleHandlerTests` | K | Locked / deleted → all in-flight quotes voided; market changed → voided. |
| `Contract/QuoteContractTests` | N | Every Acceptance Scenario from `spec.md` matches the live handlers. |

---

## 10. Definition of Done (per spec)

- All FRs implemented + traced to a passing test (matrix in `tests/B2B.Tests/coverage-matrix.md`).
- All SCs measurable; SC-001…SC-010 exercised in integration / load tests.
- AR strings flagged in `Modules/B2B/Messages/AR_EDITORIAL_REVIEW.md`; reviewer sign-off tracked.
- `openapi.b2b.json` regenerated; CI passes Guardrail #2.
- Constitution + ADR fingerprint present on PR (Guardrail #3).
- Audit-log spot-check script (`scripts/audit-spot-check-b2b.sh`) confirms every transition path produced expected `audit_log_entries` rows (P25).
- No `[NEEDS CLARIFICATION]` left in this folder.
