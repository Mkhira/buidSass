# Tasks — Phase 8: B2B

## Block A — Data + widgets

### T-8.1 · QuotesGateway
- **Files:** `features/b2b/data/quotes_gateway*.dart`.
- **DoD:** unit tests for 13 quote ops.

### T-8.2 · CompaniesGateway
- **Files:** `features/b2b/data/companies_gateway*.dart`.
- **DoD:** unit tests for 7 company ops.

### T-8.3 · LegacyQuotationsGateway
- **Files:** `features/b2b/data/legacy_quotations_gateway*.dart`.
- **DoD:** unit tests for 4 ops; 404 handled gracefully.

### T-8.4 · Shared widgets
- **Files:** `features/b2b/widgets/{quote_state_pill,quote_version_timeline,quote_actions_toolbar,member_row,role_picker}.dart`.
- **DoD:** golden tests; actions toolbar gated by `actions.*` map.

## Block B — Quotes

### T-8.5 · MyQuotesBloc + screen (S-8.1)
- **DoD:** S-8.1 criteria green.

### T-8.6 · AwaitingApprovalBloc + screen (S-8.2)
- **DoD:** S-8.2 criteria green; route guard for non-approvers.

### T-8.7 · QuoteFromCartBloc + screen (S-8.3)
- **DoD:** S-8.3 criteria green; Idempotency-Key once per intent.

### T-8.8 · QuoteFromProductBloc + screen (S-8.4)
- **DoD:** S-8.4 criteria green.

### T-8.9 · QuoteDetailBloc + screen + actions (S-8.5)
- **DoD:** S-8.5 criteria green; action gating verified for all `actions.*` combos.

### T-8.10 · QuoteDocumentBloc + download + share (S-8.6)
- **DoD:** S-8.6 criteria green; reuses Phase 6 PDF caching pattern.

## Block C — Companies

### T-8.11 · CompanyRegisterBloc + screen (S-8.7)
- **DoD:** S-8.7 criteria green; Idempotency-Key once per intent.

### T-8.12 · CompanyProfileBloc + screen (S-8.8)
- **DoD:** S-8.8 criteria green; role-based read-only enforced.

### T-8.13 · BranchesBloc + screen (S-8.9)
- **DoD:** S-8.9 criteria green; admin-only.

### T-8.14 · InviteUserBloc + screen (S-8.10)
- **DoD:** S-8.10 criteria green; admin-only.

### T-8.15 · InvitationAcceptBloc + deep-link screen (S-8.11)
- **Steps:** deep-link registration; cold-start handling.
- **DoD:** S-8.11 criteria green.

### T-8.16 · MembershipsBloc + screen (S-8.12)
- **DoD:** S-8.12 criteria green; cannot demote last admin.

## Block D — Legacy

### T-8.17 · LegacyQuotationsListBloc + screen (S-8.legacy.1)
- **DoD:** S-8.legacy.1 criteria green; menu hidden when empty.

### T-8.18 · LegacyQuotationDetailBloc + screen (S-8.legacy.2)
- **DoD:** S-8.legacy.2 criteria green; confirm modal on Accept.

## Block E — Phase exit

### T-8.19 · Analyze + tests
- **DoD:** zero warnings; tests green; matrix test of (role × quote-state) for actions toolbar.

### T-8.20 · Update overview doc + close out
- **DoD:** Phase 8 → **Done**; overall §8 status row Phase 8 flipped; final smoke test recorded.

## Screen ↔ task map

| Screen | Task |
|---|---|
| S-8.1 My quotes | T-8.5 |
| S-8.2 Awaiting approval | T-8.6 |
| S-8.3 Quote from cart | T-8.7 |
| S-8.4 Quote from product | T-8.8 |
| S-8.5 Quote detail | T-8.9 |
| S-8.6 Quote document | T-8.10 |
| S-8.7 Company register | T-8.11 |
| S-8.8 Company profile | T-8.12 |
| S-8.9 Branches | T-8.13 |
| S-8.10 Invite user | T-8.14 |
| S-8.11 Invitations deep link | T-8.15 |
| S-8.12 Memberships | T-8.16 |
| S-8.legacy.1 | T-8.17 |
| S-8.legacy.2 | T-8.18 |
| Exit | T-8.19, T-8.20 |
