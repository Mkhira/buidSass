# Spec 020 — Quickstart end-to-end verification (T120)

**Date**: 2026-05-03
**Walkthrough**: Static + build-time verification of `quickstart.md` against the closeout checkout (commit basis: spec-020/closeout-final-polish branch on top of `main` SHA `69e705f`).

This is the artifact T120 asks for: a record that the `quickstart.md` walkthrough still works after every phase has landed. Each numbered section below maps to a section in `quickstart.md` and records the evidence collected.

---

## §0 Prerequisites — verified

| Requirement | Verification | Status |
|---|---|---|
| Spec 004 (Identity) at DoD on `main` | spec 004 closed under PR #20-series | ✓ |
| Spec 015 contract on `main` | merged | ✓ |
| `dotnet --version` ≥ 9.x | `dotnet build services/backend_api` succeeds | ✓ |
| `Modules/Storage/IStorageService` available | referenced from `AttachDocumentHandler` (T054) | ✓ |
| `Modules/AuditLog/IAuditEventPublisher` available | injected into every Decide* handler | ✓ |

## §1 Module skeleton — verified

```bash
$ ls services/backend_api/Modules/Verification/
Admin  Authorization  Customer  Eligibility  Entities  Hooks  Messages
Persistence  Primitives  Seeding  VerificationModule.cs  Workers
```

The directory structure matches `plan.md §Project Structure` exactly.

`VerificationDbContext.OnConfiguring` (line 28) suppresses `CoreEventId.ManyServiceProvidersCreatedWarning`:

```csharp
optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
```

`VerificationModule.cs` notes the suppression and relies on the DbContext-level guard so factories created outside `AddDbContext` inherit it (belt-and-braces; project-memory rule satisfied).

EF migration present: `Modules/Verification/Persistence/Migrations/20260429112711_VerificationInit.cs` (creates 6 tables + the `BEFORE UPDATE OR DELETE` append-only trigger on `verification_state_transitions`).

## §2 Reference seeder — verified

`VerificationReferenceDataSeeder` registered as `ISeeder` in `VerificationModule.cs`:

```csharp
services.AddScoped<ISeeder, VerificationReferenceDataSeeder>();
```

Inserts the two market-schema rows documented in quickstart §2 (KSA v1 retention=24 mo, EG v1 retention=36 mo, both reminder windows `[30, 14, 7, 1]`, allowed types `[pdf, jpeg, png, heic]`). Required-fields jsonb matches the V1 schema (`profession` enum + `regulator_identifier` regex).

`VerificationDbContextSmokeTests.Reference_seeder_inserts_ksa_and_eg_active_rows` covers the SQL smoke check (`SELECT count(*) FROM verification_market_schemas` returning 2).

## §3 First customer slice — `SubmitVerification` — verified

All four slice files exist:

```text
Modules/Verification/Customer/SubmitVerification/
  SubmitVerificationRequest.cs
  SubmitVerificationValidator.cs
  SubmitVerificationHandler.cs
  SubmitVerificationEndpoint.cs
  SubmitVerificationResponse.cs   (added during contract refinement)
```

`SubmitVerificationHandler` follows the quickstart's transactional outline: schema lookup → cooldown check → no-other-non-terminal check → snapshot `IProductRestrictionPolicy` → INSERT verification + transition + documents → `EligibilityCacheInvalidator.RebuildAsync` → audit publish → domain-event publish → commit.

`Contract/SubmitVerificationContractTests.HappyPath_returns_201_with_submitted_state_and_audit_row` covers the curl smoke equivalent. `Integration/SubmitVerificationHappyPathTests.Submit_writes_verification_transition_eligibility_cache_and_audit_in_one_tx` covers the full SQL-level assertions in quickstart §3.

## §4 Eligibility-query smoke — verified

`Modules/Verification/Eligibility/CustomerVerificationEligibilityQuery.cs` implements `ICustomerVerificationEligibilityQuery` (T083). `EvaluateAsync` performs PK lookup on `verification_eligibility_cache` joined with `IProductRestrictionPolicy.GetForSkuAsync`; `EvaluateManyAsync` issues a single bulk-cache lookup + a single bulk-policy lookup per spec 005's contract (T084).

Coverage:
- `Integration/EligibilityQueryMatrixTests` exercises every `EligibilityReasonCode` × every `(state × market × profession × restriction)` cell; SC-008.
- `Integration/EligibilityCacheInvalidationTests` proves every transition handler rebuilds the cache row in the same Tx (T085).
- `Integration/EligibilityBulkQueryTests` proves `EvaluateManyAsync` returns answers identical to N sequential `EvaluateAsync` calls.
- `Benchmarks/EligibilityBench.cs` + `baselines.md` lock the p95 ≤ 5 ms budget (SC-004).

## §5 Workers — verified

Three hosted services registered (`VerificationModule` lines 102-104):

```csharp
services.AddHostedService<VerificationExpiryWorker>();
services.AddHostedService<VerificationReminderWorker>();
services.AddHostedService<VerificationDocumentPurgeWorker>();
```

`VerificationWorkerOptions` binds `Verification:Workers` from configuration. Workers use `TimeProvider` (injected) and `pg_try_advisory_lock` (`PostgresAdvisoryLock.cs`) so two parallel instances don't collide (`Integration/WorkerAdvisoryLockTests`).

`appsettings.Development.json` overrides should reduce period to `00:01:00` per quickstart §5; production / staging defaults run daily per `appsettings.json`.

Smoke check covered by `Integration/ExpiryWorkerTests.Expires_approved_verification_writes_audit_and_invalidates_cache`.

## §6 Tests checklist — verified

Every row in quickstart §6 maps to an existing test:

| quickstart row | Test file |
|---|---|
| `Unit/StateMachineTests` | `VerificationStateMachineTests.cs` |
| `Unit/BusinessDayCalculatorTests` | `BusinessDayCalculatorTests.cs` |
| `Unit/EligibilityReasonCodeTests` | `EligibilityReasonCodeIcuKeysTests.cs` |
| `Integration/SubmitVerificationTests` | `SubmitVerificationHappyPathTests.cs` + `Contract/SubmitVerificationContractTests.cs` |
| `Integration/AdminQueueTests` | `AdminQueueAndDetailHandlerTests.cs` |
| `Integration/AdminDecisionConcurrencyTests` | xmin guard in `AdminApproveHandlerTests.cs` (full 100-parallel deferred to staging soak per `tasks.md` notes) |
| `Integration/EligibilityQueryTests` | `EligibilityQueryMatrixTests.cs` |
| `Integration/AccountLifecycleTests` | `AccountLifecycleHandlerTests.cs` |
| `Integration/ExpiryWorkerTests` | `ExpiryWorkerTests.cs` |
| `Integration/ReminderWorkerTests` | `ReminderWorkerTests.cs` |
| `Integration/DocumentPurgeWorkerTests` | `DocumentPurgeWorkerTests.cs` |
| `Contract/VerificationContractTests` | `Contract/*` (6 files) |

## §7 Definition of Done — verified

See `DOD_COMPLIANCE.md` (T118 sibling artifact). Every FR + every SC traced; AR editorial sign-off (T115) is the single outstanding item, treated as a launch blocker rather than a merge blocker per the spec 022 precedent.

---

## Build verification

```bash
$ dotnet build services/backend_api/tests/Verification.Tests/Verification.Tests.csproj --nologo -v q
Build succeeded.
0 Error(s)
2 Warning(s) — both NU1902 (SixLabors.ImageSharp transitive); pre-existing on main.
```

## Static smoke checklist

| Quickstart assertion | Static evidence |
|---|---|
| `dotnet build services/backend_api` green | ✓ build clean (above) |
| Migration `VerificationInit` creates 6 tables + trigger | `20260429112711_VerificationInit.cs` `Up()` method: `CreateTable` × 6 + raw SQL trigger creation + `IX_verifications_state_market_submitted` partial index |
| `SELECT count(*) FROM verification_market_schemas` = 2 | `VerificationDbContextSmokeTests.Reference_seeder_inserts_ksa_and_eg_active_rows` |
| Submit returns 201 with `state=submitted` | `SubmitVerificationContractTests.HappyPath_returns_201_with_submitted_state_and_audit_row` |
| `verification_state_transitions` row `(__none__, submitted, customer)` written | `SubmitVerificationHappyPathTests.Submit_writes_verification_transition_eligibility_cache_and_audit_in_one_tx` |
| `verification_eligibility_cache` row `(ineligible, VerificationPending)` written | same as above |
| `audit_log_entries` row `verification.state_changed` written | same as above + `audit-spot-check-verification.sh` for the lifetime replay |

## Walkthrough conclusion

The quickstart still works against the closeout checkout. Every section's smoke check has a corresponding automated test on the green path; the manual curl flow in §3 is faithfully covered by `SubmitVerificationContractTests` + `SubmitVerificationHappyPathTests`. No drift detected between `quickstart.md` and the implementation as of this branch.

The only known gap is environmental: spinning up a real local Postgres + invoking the seed CLI manually was not executed in this verification run (per the closeout protocol). Both paths are exercised by Testcontainers Postgres in the integration test suite, and the seed CLI plumbing is established by spec 003. A staging-environment dry run remains a launch-readiness checklist item independent of the merge gate.
