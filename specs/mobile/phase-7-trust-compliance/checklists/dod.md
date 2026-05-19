# DoD Checklist — Phase 7: Trust & Compliance

## Verification

- [ ] `VerificationGateway` covers 8 endpoints; submit + resubmit + renew use Idempotency-Key on intent boundaries.
- [ ] Dynamic schema form renders text/number/enum/date/doc field types.
- [ ] Document upload: per-slot progress; bounded parallelism (≤2).
- [ ] Requested-info checklist surfaced; Resubmit CTA enabled once addressed.
- [ ] State transitions (submitted → info_requested → approved | rejected | expired) all rendered cleanly.

## Reviews

- [ ] `ReviewsCustomerGateway` covers 6 endpoints.
- [ ] Review submit gated by verified-buyer; 403 surfaces friendly state.
- [ ] Edit gated by `editableUntil`.
- [ ] Report reasons from server.

## Cross-cutting

- [ ] AR + EN editorial copy throughout.
- [ ] No client-side schema assumptions (BR-1).
- [ ] Telemetry never captures verification field values or review comment text.

## Phase exit

- [ ] `flutter analyze` clean.
- [ ] `flutter test` green.
- [ ] Smoke test recorded.
- [ ] §8 row → **Done**.
