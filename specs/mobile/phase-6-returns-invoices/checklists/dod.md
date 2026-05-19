# DoD Checklist — Phase 6: Returns & Invoices

## Returns

- [ ] `ReturnsGateway` covers list/detail/photos/create with Idempotency-Key on create.
- [ ] Return wizard requires eligibility + at least one selected line.
- [ ] Photo upload tiles support add / cancel / retry; checksum-based dedupe on server.
- [ ] Return detail renders timeline + photos gallery + refund + rejection reason.

## Invoices

- [ ] `InvoicesGateway` returns typed preview + binary PDF.
- [ ] Preview shows VAT rate explicitly.
- [ ] PDF caches to temp dir; Open + Share work on iOS + Android.
- [ ] 404 (not yet available) handled with friendly empty state.

## Cross-cutting

- [ ] PDF cache sweeper removes files > 30 days on app start.
- [ ] No client-side tax math.

## Phase exit

- [ ] `flutter analyze` clean.
- [ ] `flutter test` green.
- [ ] Smoke test recorded.
- [ ] §8 row → **Done**.
