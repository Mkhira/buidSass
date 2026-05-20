# Tasks — Phase 6: Returns & Invoices

## T-6.1 · ReturnsGateway
- **Files:** `features/returns/data/*`.
- **Status:** Done
- **DoD:** unit tests for 4 endpoints.

## T-6.2 · InvoicesGateway
- **Files:** `features/invoices/data/*`.
- **Status:** Done
- **DoD:** unit tests for 2 endpoints; binary PDF returned as `Uint8List`.

## T-6.3 · ReturnsListBloc + screen (S-6.1)
- **Status:** Done
- **DoD:** S-6.1 criteria green.

## T-6.4 · ReturnWizardBloc + screen + photo upload (S-6.2)
- **Steps:** parallel photo upload Bloc; Idempotency-Key per upload + per create.
- **Status:** Done
- **DoD:** S-6.2 criteria green; integration test for resume-on-failure.

## T-6.5 · ReturnDetailBloc + screen (S-6.3)
- **Status:** Done
- **DoD:** S-6.3 criteria green; gallery + rejection-reason rendering.

## T-6.6 · InvoicePreviewBloc + screen (S-6.4)
- **Status:** Done
- **DoD:** S-6.4 criteria green; 404-not-yet-available state.

## T-6.7 · InvoicePdfBloc + open/share (S-6.5)
- **Status:** Done
- **DoD:** S-6.5 criteria green; disk-full error path.

## T-6.8 · Analyze + tests
- **Status:** Done
- **DoD:** zero warnings; tests green.

## T-6.9 · Update overview doc
- **Status:** Done
- **DoD:** Phase 6 → **Done**.

## Screen ↔ task map

| Screen | Task |
|---|---|
| S-6.1 Returns list | T-6.3 |
| S-6.2 Return wizard | T-6.4 |
| S-6.3 Return detail | T-6.5 |
| S-6.4 Invoice preview | T-6.6 |
| S-6.5 Invoice PDF | T-6.7 |
| Exit | T-6.8, T-6.9 |
