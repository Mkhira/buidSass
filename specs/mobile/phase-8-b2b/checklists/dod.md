# DoD Checklist — Phase 8: B2B

## Data

- [ ] `QuotesGateway` covers 13 quote ops; from-cart + from-product use Idempotency-Key.
- [ ] `CompaniesGateway` covers 7 ops; companies POST uses Idempotency-Key.
- [ ] `LegacyQuotationsGateway` covers 4 ops; 404 / empty handled.

## Widgets

- [ ] `QuoteStatePill` renders 8 states.
- [ ] `QuoteVersionTimeline` renders all versions with publish dates.
- [ ] `QuoteActionsToolbar` reads `actions.*` map; never inspects state to decide gating.
- [ ] `RolePicker` enum from server.

## Screens

- [ ] S-8.1 My quotes: filter, pagination, state pill.
- [ ] S-8.2 Awaiting approval: approver-only route guard.
- [ ] S-8.3 Quote from cart: Idempotency-Key once per intent.
- [ ] S-8.4 Quote from product: Idempotency-Key once per intent.
- [ ] S-8.5 Quote detail + actions: action gating verified across all `actions.*` combos.
- [ ] S-8.6 Quote document: AR + EN download; cached; share-sheet works.
- [ ] S-8.7 Company register: Idempotency-Key once per intent.
- [ ] S-8.8 Company profile: role-based read-only.
- [ ] S-8.9 Branches: admin-only.
- [ ] S-8.10 Invite user: admin-only.
- [ ] S-8.11 Invitation accept: deep-link cold-start works.
- [ ] S-8.12 Memberships: admin-only; cannot demote last admin.
- [ ] S-8.legacy.1 Legacy quotations list: hidden when empty.
- [ ] S-8.legacy.2 Legacy detail + accept/reject: confirm modal on Accept.

## Cross-cutting

- [ ] All quote/company create endpoints carry Idempotency-Key.
- [ ] Role gating consistent across all admin-only screens.
- [ ] Deep-link routing for invitation tokens works cold-start.

## Phase exit

- [ ] `flutter analyze` clean.
- [ ] `flutter test` green; matrix test of (role × quote-state) for actions toolbar.
- [ ] Smoke test recorded.
- [ ] §8 row → **Done**.
- [ ] All 8 phases now Done — overall mobile delivery complete.
