# DoD Checklist — Phase 8: B2B

## Data

- [x] `QuotesGateway` covers 13 quote ops; from-cart + from-product use Idempotency-Key.
- [x] `CompaniesGateway` covers 7 ops; companies POST uses Idempotency-Key.
- [x] `LegacyQuotationsGateway` covers 4 ops; 404 / empty handled.

## Widgets

- [x] `QuoteStatePill` renders 8 states.
- [x] `QuoteVersionTimeline` renders all versions with publish dates.
- [x] `QuoteActionsToolbar` reads `actions.*` map; never inspects state to decide gating.
- [x] `RolePicker` enum from server.

## Screens

- [x] S-8.1 My quotes: filter, pagination, state pill.
- [x] S-8.2 Awaiting approval: approver-only route guard.
- [x] S-8.3 Quote from cart: Idempotency-Key once per intent.
- [x] S-8.4 Quote from product: Idempotency-Key once per intent.
- [x] S-8.5 Quote detail + actions: action gating verified across all `actions.*` combos.
- [x] S-8.6 Quote document: AR + EN download; cached; share-sheet works.
- [x] S-8.7 Company register: Idempotency-Key once per intent.
- [x] S-8.8 Company profile: role-based read-only.
- [x] S-8.9 Branches: admin-only.
- [x] S-8.10 Invite user: admin-only.
- [x] S-8.11 Invitation accept: deep-link cold-start works.
- [x] S-8.12 Memberships: admin-only; cannot demote last admin.
- [x] S-8.legacy.1 Legacy quotations list: hidden when empty.
- [x] S-8.legacy.2 Legacy detail + accept/reject: confirm modal on Accept.

## Cross-cutting

- [x] All quote/company create endpoints carry Idempotency-Key.
- [x] Role gating consistent across all admin-only screens.
- [x] Deep-link routing for invitation tokens works cold-start.

## Phase exit

- [x] `flutter analyze` clean.
- [x] `flutter test` green; matrix test of (role × quote-state) for actions toolbar.
- [x] Smoke test recorded.
- [x] §8 row → **Done**.
- [x] All 8 phases now Done — overall mobile delivery complete.
