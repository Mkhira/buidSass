# DoD verification — Spec 010 Checkout v1

**Date**: 2026-04-26 · **DoD version**: 1.0 · **Constitution fingerprint**: `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62`

## Universal Core

| ID | Item | Status | Evidence |
|---|---|---|---|
| UC-1 | Acceptance scenarios pass | ✅ | `Tests/Checkout.Tests/{Contract,Integration}/` covers US1–US8: SubmitContract, ConcurrentSubmit (SC-003), Idempotency (SC-002), AuthGate (US2), RestrictedGate (US3), BankTransferFlow (US4), ShippingQuotesContract (US5), CodContract + CodMatrix (US6/SC-008), Expiry (US7/SC-006), AdminForceExpire (US8/SC-009), WebhookDedup (SC-007), DriftFlow (SC-004), SagaCompensation. |
| UC-2 | Lint + format | ⏳ CI | Build green. |
| UC-3 | Contract diff | ✅ | `openapi.checkout.json` shipped at repo root with all 11 endpoints + reason codes. |
| UC-4 | Constitution fingerprint in PR | ✅ | `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62` — to be added to PR description. |
| UC-5 | Constitution / ADR-protected paths untouched | ✅ | No edits to `.specify/memory/constitution.md` or `docs/adrs/**`. |
| UC-6 | Code-owner approvals | ⏳ Reviewer | Pending PR. |
| UC-7 | Signed commits + merge policy | ⏳ CI | Branch protection enforces. |
| UC-8 | Spec header records constitution version | ✅ | `spec.md` cites the relevant principles for v1.0.0. |

## Applicability-Tagged Items

### [trigger: state-machine] ✅
`Modules/Checkout/Primitives/CheckoutSessionStateMachine.cs` enumerates: states (`created`, `address_set`, `shipping_selected`, `payment_selected`, `priced`, `submitting`, `submitted`, `abandoned`, `expired`), transitions, allowed actors (customer, admin force-expire, expiry worker), guards (idempotent self-transition for replays), failure handling (saga compensation in `SagaCompensationTests`). Unit tests in `Tests/Checkout.Tests/Unit/CheckoutSessionStateMachineTests.cs`.

### [trigger: audit-event] ✅
Admin `ForceExpire` writes an audit row via `IAuditEventPublisher`; covered by `Tests/Checkout.Tests/Contract/Customer/AdminForceExpireTests.cs` (SC-009 — `AdminForceExpire_WritesAuditRow`). Customer-side state changes go to `checkout_state_transitions` for traceability.

### [trigger: storage] N/A
Checkout does not handle file/object storage directly — addresses are JSONB columns on the session entity.

### [trigger: pdf] N/A
Checkout does not generate PDFs.

### [trigger: user-facing-strings] ✅
`Modules/Checkout/Messages/checkout.{ar,en}.icu` — 33 keys each, parity verified (no missing keys either side). AR strings are editorial-quality first-draft; native-speaker review is the deferred T036 follow-up tracked in `tasks.md`.

### [trigger: environment-aware] ✅
`CheckoutModule.AddCheckoutModule` registers hosted services (`CheckoutExpiryWorker`, `PaymentReconciliationWorker`) only outside Test environment. SeedGuard not bypassed.

### [trigger: docker-surface] N/A
No Dockerfile changes; module compiles into existing image.

### [trigger: ships-a-seeder] N/A
Checkout ships no production seeder — `PaymentMethodCatalog` is configuration-driven, not DB-seeded.

### [trigger: ui-surface] N/A
Backend-only spec.

---

## Sign-off

- [X] All applicable DoD triggers verified.
- [X] Tests green (Checkout.Tests passes locally; full suite passes in CI).
- [X] Build clean (0 errors).
- [X] OpenAPI artefact present (`openapi.checkout.json`).
- [X] Constitution fingerprint computed and recorded.
- [ ] T036 native-speaker AR review — booked separately (deferred per `tasks.md`).
- [ ] CI checks (UC-2, UC-7) — pending PR open.
- [ ] Reviewer approvals (UC-6) — pending PR.
