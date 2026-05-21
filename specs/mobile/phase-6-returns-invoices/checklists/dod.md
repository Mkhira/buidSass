# DoD Checklist — Phase 6: Returns & Invoices

## Returns

- [x] `ReturnsGateway` covers list/detail/photos/create with Idempotency-Key on create.
- [x] Return wizard requires eligibility + at least one selected line.
- [x] Photo upload tiles support add / cancel / retry; checksum-based dedupe on server.
- [x] Return detail renders timeline + photos gallery + refund + rejection reason.

## Invoices

- [x] `InvoicesGateway` returns typed preview + binary PDF.
- [x] Preview shows VAT rate explicitly.
- [x] PDF caches to temp dir; Open + Share work on iOS + Android.
- [x] 404 (not yet available) handled with friendly empty state.

## Cross-cutting

- [x] PDF cache sweeper removes files > 30 days on app start.
- [x] No client-side tax math.

## Phase exit

- [x] `flutter analyze` clean.
- [x] `flutter test` green.
- [x] Smoke test recorded.
- [x] §8 row → **Done**.
