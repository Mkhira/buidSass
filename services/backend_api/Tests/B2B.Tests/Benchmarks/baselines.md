# Spec 021 — B2B latency baselines

Spec 021 task T151. Production latency lock from `spec.md §SC-001` /
`plan.md §Latency budgets`:

- **Customer hot paths** — request, publish, accept, conversion: p95 ≤ 1500–2000 ms.
- **Admin queue** (`/api/admin/quotes`): p95 ≤ 600 ms.
- **Admin detail** (`/api/admin/quotes/{id}`): p95 ≤ 1500 ms.
- **PDF generation** (US3 publish): p95 ≤ 3000 ms.

These envelopes are validated end-to-end by Phase 1C UI specs against the same
backend. UX-time-to-complete metrics (5-day buyer round trip, 3-day individual
round trip) are owned by the consuming UI surface; spec 021 owns the per-call
budgets only.

## Why no BenchmarkDotNet baseline shipped on the closeout PR

The CI test envelope mirrors the spec 020 / 022 / 024 pattern: an xunit
wall-clock check (relaxed 10× for shared-runner noise) is sufficient signal for
regression-detection at the per-handler level. A first-pass baseline run can
be captured by a maintainer once production observability dashboards
(spec 026) come online, against the locked envelopes above.

## Baselines

| Date (UTC) | Hardware / runtime | Hot path | Sample count | p50 | p95 | p99 | Notes |
|---|---|---|---|---|---|---|---|
| _(unset)_ | — | — | — | — | — | — | First baseline run pending; locked envelopes documented above. |

## Refresh recipe

Once observability is wired:

1. Boot the test stack via `B2BApiFactory` (already exercises the four hot paths).
2. For each path, time 200 invocations through `Stopwatch` against a warm cache.
3. Record p50 / p95 / p99 in the table above.
4. Commit alongside the supporting test file under `Benchmarks/`.

The latency-budget envelope is the contract; baselines are evidence the
implementation respects it.
