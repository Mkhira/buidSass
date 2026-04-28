# Quickstart — Implementer Walkthrough: Verification (Spec 020)

**Date**: 2026-04-28
**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/verification-contract.md](./contracts/verification-contract.md)

This is the path a Lane A engineer (or a Codex/Claude agent operating in Lane A) follows to land the first vertical slice of spec 020. It is intentionally minimal — `tasks.md` (produced by `/speckit-tasks`) is the canonical task-by-task plan; this file is the smoke-test loop that proves the module is wired correctly before slice work begins in earnest.

---

## 0. Prerequisites

- Spec 004 (Identity) at DoD on `main`.
- Spec 015 (admin-foundation) **contract** merged on `main` (the admin web shell does not need to be built before this spec's backend can land — only the contract does).
- Local Postgres running per `docs/local-setup.md`.
- `dotnet --version` ≥ 9.x.
- `Modules/Storage/IStorageService` and `IVirusScanService` available (already on `main` per spec 003).
- `Modules/AuditLog/IAuditEventPublisher` available (already on `main` per spec 003).

---

## 1. Module skeleton (Phase A + B)

Create the module folder layout exactly as in [plan.md §Project Structure](./plan.md). The minimum to build green:

```bash
services/backend_api/Modules/Verification/
  VerificationModule.cs
  Primitives/
    VerificationState.cs
    VerificationActorKind.cs
    VerificationStateMachine.cs
    EligibilityReasonCode.cs
  Entities/
    Verification.cs
    VerificationDocument.cs
    VerificationStateTransition.cs
    VerificationMarketSchema.cs
    VerificationReminder.cs
    VerificationEligibilityCache.cs
  Persistence/
    VerificationDbContext.cs
    Configurations/   (one IEntityTypeConfiguration per entity)
    Migrations/       (added by `dotnet ef migrations add VerificationInit`)
  Authorization/
    VerificationPermissions.cs
```

`VerificationModule.cs` MUST suppress `RelationalEventId.ManyServiceProvidersCreatedWarning` on its `AddDbContext` registration (project-memory rule — every new module's AddDbContext must do this or Identity tests break):

```csharp
services.AddDbContext<VerificationDbContext>((sp, opts) =>
{
    opts.UseNpgsql(connectionString);
    opts.ConfigureWarnings(w =>
        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.ManyServiceProvidersCreatedWarning));
});
```

Run the migration:

```bash
dotnet ef migrations add VerificationInit \
  --project services/backend_api \
  --context VerificationDbContext \
  --output-dir Modules/Verification/Persistence/Migrations
dotnet ef database update --context VerificationDbContext --project services/backend_api
```

**Smoke check**: `dotnet build services/backend_api` is green; the migration creates 6 tables under the `verification` schema (or default `public` schema if the project convention doesn't use per-module schemas — match the project's existing pattern).

---

## 2. Reference seeder (Phase C)

`Modules/Verification/Seeding/VerificationReferenceDataSeeder.cs` implements the `ISeeder` interface from spec 003 and inserts (idempotently) two `verification_market_schemas` rows:

```text
KSA: version=1, retention_months=24, cooldown_days=7, expiry_days=365,
     reminder_windows_days=[30,14,7,1], sla_decision_business_days=2,
     sla_warning_business_days=1, allowed_document_types=[pdf,jpeg,png,heic]
EG:  version=1, retention_months=36, cooldown_days=7, expiry_days=365,
     reminder_windows_days=[30,14,7,1], sla_decision_business_days=2,
     sla_warning_business_days=1, allowed_document_types=[pdf,jpeg,png,heic]
```

Required-fields jsonb for both markets, V1:

```json
[
  { "name": "profession", "type": "enum", "values": ["dentist","dental_lab_tech","dental_student","clinic_buyer"] },
  { "name": "regulator_identifier", "type": "text", "pattern": "^[0-9A-Z\\-]{5,20}$" }
]
```

Run via the platform seed CLI (per spec 003 conventions):

```bash
dotnet run --project services/backend_api -- seed --mode=apply --tag=verification-reference
```

**Smoke check**: `SELECT count(*) FROM verification_market_schemas` returns 2.

---

## 3. First customer slice — `SubmitVerification` (Phase D)

The minimum end-to-end vertical slice that proves the module's plumbing:

**Slice files**:
```
Modules/Verification/Customer/SubmitVerification/
  SubmitVerificationRequest.cs   (DTO with document_ids array)
  SubmitVerificationValidator.cs (FluentValidation against active market schema)
  SubmitVerificationHandler.cs   (MediatR; transactional; emits state transition + audit event + cache rebuild)
  SubmitVerificationEndpoint.cs  (Minimal API mapping POST /api/customer/verifications)
```

**Handler outline** (read `data-model.md §3` for full transition rules):

```csharp
public sealed class SubmitVerificationHandler : IRequestHandler<SubmitVerificationRequest, Guid>
{
    public async Task<Guid> Handle(SubmitVerificationRequest req, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 1. Resolve active market schema + check cool-down + check no other non-terminal.
        // 2. Snapshot IProductRestrictionPolicy (deferred — V1 catalog stub returns Unrestricted).
        // 3. Insert Verification row (state=Submitted).
        // 4. Insert VerificationStateTransition row (__none__ → Submitted).
        // 5. Re-attach documents (verify ownership + scan_status=clean + size + count).
        // 6. EligibilityCacheInvalidator.RebuildAsync(customerId, ct) — submitted does NOT confer eligibility, so this writes Ineligible:VerificationPending.
        // 7. _auditPublisher.PublishAsync(new AuditEvent("verification.state_changed", ...))
        // 8. _domainBus.Publish(new VerificationSubmitted(...))   // for spec 025
        // 9. tx.CommitAsync(ct)
        // 10. return verificationId
    }
}
```

**Smoke test** (manual, against local API):

```bash
# Assume a test customer JWT in $JWT and one already-uploaded clean document id in $DOC_ID.
curl -X POST http://localhost:5050/api/customer/verifications \
  -H "Authorization: Bearer $JWT" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -H "Accept-Language: ar" \
  -d "{\"profession\":\"dentist\",\"regulator_identifier\":\"1234567\",\"document_ids\":[\"$DOC_ID\"]}"
# Expected: 201; body.state == "submitted"
```

Then:

```sql
SELECT state, market_code FROM verifications WHERE customer_id = '<test-customer>';
-- Expect: ('submitted', 'ksa')

SELECT prior_state, new_state, actor_kind FROM verification_state_transitions
WHERE verification_id = '<the-new-id>';
-- Expect: ('__none__', 'submitted', 'customer')

SELECT eligibility_class, reason_code FROM verification_eligibility_cache
WHERE customer_id = '<test-customer>';
-- Expect: ('ineligible', 'VerificationPending')

SELECT count(*) FROM audit_log_entries WHERE event_kind = 'verification.state_changed';
-- Expect: 1 (or +1 from baseline)
```

If all four queries match, the module's plumbing — DI, DbContext, EF migrations, MediatR pipeline, audit publisher, eligibility cache invalidator, transaction boundary — is wired correctly. Every subsequent slice (admin queue, decisions, workers) layers onto this same plumbing.

---

## 4. Eligibility-query smoke (Phase F)

Once `ICustomerVerificationEligibilityQuery` is implemented:

```csharp
// In a controller or test harness:
var result = await _eligibility.EvaluateAsync(testCustomerId, "RESTRICTED-SKU-001", ct);
// With a customer in `submitted` state and a SKU that catalog flags restricted in KSA for dentist:
//   result.Class == Ineligible
//   result.ReasonCode == EligibilityReasonCode.VerificationPending
//   result.MessageKey == "verification.eligibility.pending"
```

Bulk variant:

```csharp
var results = await _eligibility.EvaluateManyAsync(testCustomerId, new[] { "SKU-1", "SKU-2", "SKU-3" }, ct);
// Catalog list page calls this once per page. Expected p95 latency on a warm cache: < 5 ms.
```

A simple latency benchmark using BenchmarkDotNet sits in `tests/Verification.Tests/Benchmarks/EligibilityBench.cs` (added in Phase F). Acceptance: p95 < 5 ms on the dev machine baseline. Production budget recheck during `/speckit-implement` integration phase.

---

## 5. Workers (Phase H)

Each worker is an `IHostedService` with a `PeriodicTimer(TimeSpan.FromHours(24))`. For local dev, override the period to `TimeSpan.FromMinutes(1)` via `appsettings.Development.json`:

```json
{
  "Verification": {
    "Workers": {
      "ExpiryCheckPeriod": "00:01:00",
      "ReminderCheckPeriod": "00:01:00",
      "DocumentPurgeCheckPeriod": "00:01:00"
    }
  }
}
```

Time is injected via `TimeProvider`. Tests use `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider` to advance the clock deterministically.

**Smoke check** for `VerificationExpiryWorker`:
1. Use `VerificationDevDataSeeder` to insert one approved verification with `expires_at = now() - interval '1 hour'`.
2. Wait for the 1-minute dev tick.
3. Assert `state = 'expired'` and an audit event was written.

---

## 6. Tests checklist (per spec — see `tests/Verification.Tests/`)

| Test | Phase | Purpose |
|---|---|---|
| `Unit/StateMachineTests` | A | Forbid terminal → non-terminal; allow defined transitions; reject unknown transitions. |
| `Unit/BusinessDayCalculatorTests` | A | Sun–Thu working week; holiday list respected; SLA arithmetic deterministic. |
| `Unit/EligibilityReasonCodeTests` | A | Every enum value has an ICU key in both `verification.en.icu` and `verification.ar.icu`. |
| `Integration/SubmitVerificationTests` | D | Happy path + every error reason code in §2.1. |
| `Integration/AdminQueueTests` | E | Filtering + sorting + market scope; no cross-market leak. |
| `Integration/AdminDecisionConcurrencyTests` | E | Two parallel approve commands → exactly one wins; loser sees `verification.already_decided`. |
| `Integration/EligibilityQueryTests` | F | Synthetic matrix `(state × market × profession × restriction)` → 100% expected outcomes (SC-008). |
| `Integration/AccountLifecycleTests` | G | Locked → void; deleted → void + expedited document purge; market change → superseded + cache flip. |
| `Integration/ExpiryWorkerTests` | H | `FakeTimeProvider`-driven; expiry transitions + `verification.expired` audit event + cache flip. |
| `Integration/ReminderWorkerTests` | H | Each window fires exactly once per verification; back-window skip writes `skipped=true` row + audit note. |
| `Integration/DocumentPurgeWorkerTests` | H | Documents past `purge_after` deleted; row preserved with `purged_at` set; audit event emitted. |
| `Contract/VerificationContractTests` | K | Every Acceptance Scenario from `spec.md` exercises a live endpoint and matches `contracts/verification-contract.md`. |

---

## 7. Definition of Done (per spec, satisfies `docs/dod.md`)

- All FRs implemented + traced to a passing test (matrix in `tests/Verification.Tests/coverage-matrix.md`).
- All SCs measurable; SC-001..SC-011 exercised in integration / load tests as relevant.
- AR strings flagged in `Modules/Verification/Messages/AR_EDITORIAL_REVIEW.md`; reviewer sign-off tracked.
- `openapi.verification.json` regenerated and CI passes Guardrail #2.
- Constitution + ADR fingerprint present on PR (Guardrail #3).
- Audit-log spot-check script (`scripts/audit-spot-check-verification.sh`) confirms every transition path produced the expected `audit_log_entries` event (P25).
- No `[NEEDS CLARIFICATION]` left in this folder.
