**Testing Strategy Version**: 1.0.0 | **Date**: 2026-04-19 | **Status**: Ratified

## Purpose

This document defines the minimum testing layers and mandatory scenario types for every spec category used in Phase 1. It aligns with `docs/dod.md`, where universal scenario expectations are mandatory acceptance requirements.

## Universal Mandatory Scenario Types

1. Every state transition in the applicable state model has at least one test.
2. Every error branch reachable in normal operation has at least one test.
3. Every permission boundary (allowed role vs denied role) has at least one test.
4. Every acceptance scenario in the spec maps to at least one test.

## Backend Domain Spec

### Required Layers

- Unit tests for MediatR handlers and domain services.
- Integration tests using Testcontainers + PostgreSQL through the full request pipeline.
- Contract diff checks on every PR using OpenAPI + oasdiff.

### Mandatory Scenario Types

- Every transition in domain state machines.
- Every error branch from validation, domain rules, and external adapters.
- Every permission-gated command/query.
- Every acceptance scenario in the owning spec.

## Flutter Customer-App Spec

### Required Layers

- Widget tests for each screen component and critical widget composition.
- Integration tests (`flutter_test`) for end-to-end flow per feature.
- RTL golden tests (at least one per screen) in Arabic locale.

### Mandatory Scenario Types

- Every acceptance scenario in the owning spec.
- Every loading, empty, and error UI state.
- Every permission-dependent UI boundary.
- Every RTL-sensitive layout transition.

## Next.js Admin Spec

### Required Layers

- Jest unit tests for components, hooks, and UI helper logic.
- Playwright E2E tests for critical admin workflows.

### Mandatory Scenario Types

- Every acceptance scenario in the owning spec.
- Every permission-gated route and action.
- Every form validation branch (valid, invalid, boundary inputs).
- Every operational error branch surfaced to admin users.

## Integration Adapter Spec

### Required Layers

- Unit tests using mocked provider adapter behavior.
- Integration tests against provider sandbox or recorded cassettes.
- Contract/schema diff checks where provider contracts are versioned.

### Mandatory Scenario Types

- Every happy-path integration flow.
- Every provider error response shape handled by the adapter.
- Every webhook replay / duplicate-event branch.
- Every timeout/retry and fallback behavior branch.

## Shared-Contract Spec

### Required Layers

- Contract diff check via oasdiff on every PR.
- No separate runtime test suite required unless the spec explicitly adds one.

### Mandatory Scenario Types

- Every breaking schema change attempt must be detected by diff checks.
- Every additive change must preserve existing consumer compatibility.
- Every acceptance scenario tied to contract generation or consumption.
- Every permission and state constraint expressed in contracts must remain represented.
