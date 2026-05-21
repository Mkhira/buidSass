# DoD Checklist — Phase 7: Trust & Compliance

## Verification

- [x] `VerificationGateway` covers 8 endpoints; submit + resubmit + renew use Idempotency-Key on intent boundaries.
- [x] Dynamic schema form renders text/number/enum/date/doc field types.
- [x] Document upload: per-slot progress; bounded parallelism (≤2).
- [x] Requested-info checklist surfaced; Resubmit CTA enabled once addressed.
- [x] State transitions (submitted → info_requested → approved | rejected | expired) all rendered cleanly.

## Reviews

- [x] `ReviewsCustomerGateway` covers 6 endpoints.
- [x] Review submit gated by verified-buyer; 403 surfaces friendly state.
- [x] Edit gated by `editableUntil`.
- [x] Report reasons from server.

## Cross-cutting

- [x] AR + EN editorial copy throughout.
- [x] No client-side schema assumptions (BR-1).
- [x] Telemetry never captures verification field values or review comment text.

## Phase exit

- [x] `flutter analyze` clean.
- [x] `flutter test` green.
- [x] Smoke test recorded.
- [x] §8 row → **Done**.
