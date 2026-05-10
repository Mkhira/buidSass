# Research: 027 — Payments Integration

**Phase**: 0
**Date**: 2026-05-10

## §1 — Hosted-fields integration pattern
**Decision**: For each card provider (HyperPay, Tap, Paymob, Kashier), the customer's PAN/CVV is entered into a provider-hosted iframe / SDK; only the resulting token is passed to our backend. Our checkout page calls `POST /payments` to obtain a `session_id` + provider public key, embeds the provider's hosted-fields SDK with those parameters, and on submit calls our backend with the token only.
**Rationale**: SAQ-A boundary requires that PAN never enter our origin. Hosted-fields satisfies this; redirect-based flows would be SAQ-A-EP. v1 chooses hosted-fields universally.
**Alternatives**: Full-redirect (rejected, falls under SAQ-A-EP), API-direct (rejected, expands PCI scope to SAQ-D).

## §2 — BNPL redirect flow standardization
**Decision**: Tabby, Tamara, Valu all use redirect-based flows. We standardize on a single `pending_external_redirect` state plus a per-provider TTL (default 10 min). On webhook, we transition to `captured` (or `failed`/`expired`).
**Rationale**: Same UX pattern across BNPL providers reduces frontend complexity. TTL chosen long enough for credit decisions but short enough that abandoned carts don't leave indefinite open state.
**Alternatives**: Per-provider state machines (rejected — adds complexity without value).

## §3 — Idempotency-key derivation
**Decision**: `idempotency_key = sha256(order_id + method + attempt_id)`, where `attempt_id` is a UUID generated client-side at attempt-create time. Stored in `idempotency_keys` cache table for fast lookup. The provider call also carries the `idempotency_key` as a header where the provider supports it (HyperPay does, Paymob does, Tabby does).
**Rationale**: Prevents double-charges on customer double-click and on network retries. Cross-provider header passing extends idempotency to provider side.
**Alternatives**: Pure DB unique constraint (rejected — slower lookup; complicates the user-facing 'duplicate' response).

## §4 — Reconciliation-ledger fetch patterns per provider
**Decision**: Per-provider, with a capability matrix:
- HyperPay: API + signed-CSV; we use API.
- Tap: API.
- Paymob: API + dashboard CSV (we automate via API).
- Kashier: CSV via SFTP at v1 (their API doesn't expose settlement at v1).
- Tabby: API.
- Tamara: API.
- Valu: CSV via SFTP at v1.

CSV-based providers route through a separate `CsvSettlementFetcher` that pulls from SFTP, parses, and feeds the same matcher pipeline.
**Rationale**: Pragmatic — match the provider's actual capabilities at v1. Migrate Kashier/Valu to API once available.
**Alternatives**: Defer CSV providers to 1.5 (rejected — Kashier is an EG card backup; Valu is the EG BNPL primary).

## §5 — PCI SAQ-A boundary verification
**Decision**: Three layers of enforcement.
1. **Schema-level**: a CI guard `check-pci-scope.sh` greps the migration files + EF entity definitions for cardholder-shaped column names (PAN, CVV, track1, track2, primary_account_number, card_number).
2. **Egress-level**: `EgressPayloadFilter` (a request-pipeline component injected into every `IPaymentProvider` implementation) validates that outgoing payloads carry only allow-listed fields (token, amount, currency, customer_ref, recipient_name, masked_phone). Rejects if extras are present.
3. **Runtime-level**: `PciScopeMonitor` runs nightly, queries the `payments` schema for any column matching cardholder regex, alerts on hit.
**Rationale**: Defense-in-depth; CI catches PR-time, egress-filter catches runtime, monitor catches data-shape drift.
**Alternatives**: Single layer (rejected — single layers fail silently).

## §6 — Retry creates new row vs in-place state mutation
**Decision**: Each retry attempt is a NEW Payment row referencing the same `order_id`. The order's `payment_status` field reflects the latest attempt's state (resolved by `ORDER BY created_at DESC LIMIT 1` query).
**Rationale**: Audit clarity — every attempt has its own immutable history (provider response, idempotency key, etc.). In-place mutation would lose attempt history.
**Alternatives**: In-place with attempt history table (rejected — duplicates row's contents into history; harder to query).

## §7 — Bank-transfer reference scheme
**Decision**: Reference format: `<MARKET>-<UUID4-FIRST-8>-<SHORT-HASH>`, e.g., `SA-3a7f9c12-X8K2`. Stored in `bank_transfer_references`. Operator matching is text-search on the bank statement entry's "memo" / "remitter reference" field.
**Rationale**: Human-readable reference fits in bank-statement memo fields (most allow 16–32 chars); UUID prefix uniquifies; market prefix aids manual sorting.
**Alternatives**: Pure UUID (rejected — too long), sequential integer (rejected — guessable, statement-collision risk).

## §8 — Webhook replay capability matrix
**Decision**: Per-provider:
- HyperPay: ✅ events API supports time-range fetch.
- Tap: ✅
- Paymob: ✅
- Kashier: ✅ via dashboard export only at v1; our replay tool requests manual export
- Tabby: ✅
- Tamara: ✅
- Valu: ❌ at v1 — manual reconciliation fallback documented in runbook

The `IPaymentProvider.ReplayWebhooks` method returns `NotSupported` for Valu; the operator UI surfaces the limitation.
**Rationale**: BR-16 requires we surface limitations honestly; manual reconciliation is the documented fallback for non-replay providers.

---

All eight resolutions decided. No NEEDS CLARIFICATION remaining.
