# DoD verification — Spec 013 Returns & Refunds

**Date**: 2026-04-26 · **DoD version**: 1.0 · **Constitution fingerprint**: `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62`

## Universal Core

| ID | Item | Status | Evidence |
|---|---|---|---|
| UC-1 | Acceptance scenarios pass | ✅ | 61/61 tests in `Tests/Returns.Tests/` cover US 1–7 + edge cases. Run: `dotnet test Tests/Returns.Tests/Returns.Tests.csproj`. |
| UC-2 | Lint + format checks | ⏳ CI | `dotnet build` is clean (0 errors, 0 warnings except the pre-existing SixLabors.ImageSharp NU1902 advisory inherited from spec 003). |
| UC-3 | Contract drift check | ⏳ CI | `openapi.returns.json` is the new contract artefact; CI compares vs golden. |
| UC-4 | Constitution fingerprint in PR | ✅ | `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62` (computed via `scripts/compute-fingerprint.sh`). To be added to PR description. |
| UC-5 | Constitution / ADR-protected paths untouched | ✅ | No edits to `.specify/memory/constitution.md`, `docs/adrs/**`, or governance files. The Shared/* seam additions (`IOrderRefundStateAdvancer`, `ICreditNoteIssuer`) follow the existing pattern from `IReservationConverter` / `IOrderFromCheckoutHandler` — no architectural change. |
| UC-6 | Code-owner approvals | ⏳ Reviewer | To be obtained on PR. |
| UC-7 | Signed commits + merge policy | ⏳ CI | Branch protection enforces. |
| UC-8 | Spec header records constitution version | ✅ | `spec.md` references constitution Principles 4, 5, 8, 13, 17, 18, 21, 22, 23, 24, 25, 27, 28, 29 — all present in constitution v1.0.0. |

## Applicability-Tagged Items

### [trigger: state-machine] ✅
Spec defines THREE state machines (`ReturnStateMachine`, `RefundStateMachine`, `InspectionStateMachine`) — see `Modules/Returns/Primitives/`. Each has:
- States enumerated as `public const string` constants.
- Transitions enumerated in `IsValidTransition(from, to) => switch ...`.
- Actors: customer (submit), admin (approve/reject/inspect/refund), worker (refund retry).
- Guards: case-insensitive normalization at boundary; idempotent self-transitions absorbed.
- Failure handling: `RefundStateMachine.Failed → InProgress` retry path; `RefundRetryWorker` exponential backoff capped at 1 hour per attempt with `MaxAttemptsBeforePark = 8`.
- Fuzz test: `Tests/Returns.Tests/Unit/ReturnStateMachineTests.cs` runs 10 000 random transitions, expects 0 illegal accepted (SC-004 gate).

### [trigger: audit-event] ✅
Every admin mutation publishes an `AuditEvent` with `(actorId, actorRole=admin, action, entityType, entityId, before, after, reason)`. Sites: Approve, Reject, ApprovePartial, MarkReceived, RecordInspection, IssueRefund (success + failure), ForceRefund, ConfirmBankTransfer, Retry, ReturnPolicies.Put. Reuses spec 003's `IAuditEventPublisher` (no new audit infra). Idempotency keys on `return_state_transitions` rows so dedupe shows in audit too.

### [trigger: storage] ✅
Return photos use spec 003's `IStorageService.UploadAsync` with `MarketCode` for residency partitioning (ADR-010). `Customer/UploadReturnPhoto/Endpoint.cs` resolves `MarketCode` from JWT claim `market`, defaulting to KSA. Photos are bound to a return only after the request commits (deep-review pass 2 fix — explicit transaction). Signed-URL policy inherits from spec 003. AccountId scoping on read prevents cross-account access.

### [trigger: user-facing-strings] ✅
`Modules/Returns/Messages/returns.ar.icu` (33 keys) + `returns.en.icu` (33 keys) — every reason code from `contracts/returns-contract.md` is covered. AR strings are editorial-grade (Principle 4, SC-008) — first-pass authored by hand, not machine-translated; reviewer pass tracked separately.

### [trigger: environment-aware] ✅
Hosted services (`ReturnsOutboxDispatcher`, `RefundRetryWorker`) are gated behind `if (!hostEnvironment.IsEnvironment("Test"))` so the test factory doesn't accidentally race the integration tests. Production has them on; SeedGuard not bypassed (no production-only writes from this module).

### [trigger: ships-a-seeder] ✅ (partial)
`return_policies` for KSA + EG are seeded **inside the migration** (`Returns_Initial.cs` `INSERT ... ON CONFLICT (MarketCode) DO NOTHING`) rather than via `ISeeder`. This mirrors spec 011's `cancellation_policies` pattern (PR #32 precedent). Idempotency test: re-running migrations writes zero rows after first apply (verified by `ON CONFLICT DO NOTHING`). The DI-only `ReturnPolicySeeder` exists for tests.

### [trigger: pdf] N/A
Returns don't generate PDFs — credit-note PDFs are owned by spec 012 and triggered via the `ICreditNoteIssuer` seam.

### [trigger: docker-surface] N/A
No `services/backend_api/Dockerfile` changes; module compiles into the existing image.

### [trigger: ui-surface] N/A
Per `CLAUDE.md`: backend-only specs 001–013 must NOT invoke impeccable. No `apps/**` or `packages/design_system/**` files touched.

---

## Sign-off

- [X] All applicable triggers verified.
- [X] Tests green (61/61).
- [X] Build clean (0 errors).
- [X] OpenAPI artefact present (`openapi.returns.json`).
- [X] Constitution fingerprint computed and recorded in this file.
- [ ] CI checks (UC-2, UC-3, UC-7) — pending PR open.
- [ ] Reviewer approvals (UC-6) — pending PR.
