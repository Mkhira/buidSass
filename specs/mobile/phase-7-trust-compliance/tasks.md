# Tasks — Phase 7: Trust & Compliance

## T-7.1 · VerificationGateway
- **Files:** `features/verification/data/*`.
- **Status:** Done
- **DoD:** unit tests for 8 endpoints.

## T-7.2 · ReviewsCustomerGateway
- **Files:** `features/reviews/data/*` (extends Phase 2's aggregates).
- **Status:** Done
- **DoD:** unit tests for 6 customer endpoints.

## T-7.3 · VerificationListBloc + screen (S-7.1)
- **Status:** Done
- **DoD:** S-7.1 criteria green.

## T-7.4 · VerificationSubmitBloc + dynamic form (S-7.2)
- **Status:** Done
- **DoD:** S-7.2 criteria green; widget test renders all field types.

## T-7.5 · VerificationDetailBloc + screen + document upload (S-7.3)
- **Status:** Done
- **DoD:** S-7.3 criteria green; bounded parallel upload.

## T-7.6 · Resubmit + Renew (S-7.4)
- **Status:** Done
- **DoD:** S-7.4 criteria green.

## T-7.7 · ReviewSubmitBloc + screen (S-7.5)
- **Status:** Done
- **DoD:** S-7.5 criteria green; 403 not-eligible state.

## T-7.8 · MyReviewsBloc + list screen (S-7.6)
- **Status:** Done
- **DoD:** S-7.6 criteria green.

## T-7.9 · MyReviewDetailBloc + edit screen (S-7.7)
- **Status:** Done
- **DoD:** S-7.7 criteria green; edit gating by `editableUntil`.

## T-7.10 · ReportReviewBloc + screen (S-7.8)
- **Status:** Done
- **DoD:** S-7.8 criteria green.

## T-7.11 · Analyze + tests
- **Status:** Done
- **DoD:** zero warnings; tests green.

## T-7.12 · Update overview doc
- **Status:** Done
- **DoD:** Phase 7 → **Done**.

## Screen ↔ task map

| Screen | Task |
|---|---|
| S-7.1 Verification list | T-7.3 |
| S-7.2 Verification submit | T-7.4 |
| S-7.3 Verification detail + docs | T-7.5 |
| S-7.4 Resubmit / Renew | T-7.6 |
| S-7.5 Review submit | T-7.7 |
| S-7.6 My reviews list | T-7.8 |
| S-7.7 My review detail / edit | T-7.9 |
| S-7.8 Report review | T-7.10 |
| Exit | T-7.11, T-7.12 |
