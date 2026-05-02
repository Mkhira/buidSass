# Verification eligibility-query latency baselines

Spec 020 task T119 — perf-verification artifact. Production latency lock per
research §R1 / SC-004 / data-model §6:

- **Warm-cache `EvaluateAsync`**: p95 ≤ 5 ms, p99 ≤ 15 ms.
- **Bulk `EvaluateManyAsync`** (50 SKUs): p95 ≤ 25 ms, p99 ≤ 75 ms (5× single-SKU
  budget; reflects the single round-trip's better amortization).

The CI test envelope at `Tests/Verification.Tests/Benchmarks/EligibilityBench.cs`
runs against Testcontainers Postgres and is intentionally relaxed by 10× to
absorb shared-runner noise without flapping. Production observability dashboards
(spec 026 once it lands) enforce the locked envelope.

## Baselines

| Date (UTC) | Hardware / runtime | Sample count | p50 | p95 | p99 | Notes |
|---|---|---|---|---|---|---|
| 2026-05-02 | Docker Desktop on Apple Silicon, .NET 9.0.10, Postgres 16-alpine in container | 200 | _populate from `dotnet test` console output_ | _idem_ | _idem_ | First baseline taken on the spec 020 closeout PR. |

To refresh: `dotnet test Tests/Verification.Tests/Verification.Tests.csproj --filter "FullyQualifiedName~EligibilityBench" --logger "console;verbosity=normal"` and copy the `[EligibilityBench]` line.

## Why a regular xunit test, not BenchmarkDotNet

The original task called for BenchmarkDotNet. The repo does not currently take
a BenchmarkDotNet dependency, and the per-task framing
(`"may relax in CI environments per project convention"`) makes a regular
xunit wall-clock check a reasonable substitute. Rationale:

1. **No new dependency.** A single xunit test reuses the Testcontainers fixture
   already present in this project; BenchmarkDotNet would add ~30 MB of NuGet
   packages and a separate runner mode for one test.
2. **Single-file readable contract.** `EligibilityBench.cs` documents exactly
   what is measured (warm-cache `EvaluateAsync` path), the budgets, and the
   relaxed-CI rationale at the call site. BenchmarkDotNet's attribute-driven
   model splits that across more places.
3. **CI noise.** BenchmarkDotNet's defaults run multiple iterations per
   benchmark with statistical analysis; on shared CI runners that adds wall
   time without changing the pass/fail signal we actually care about, which
   is a coarse "did the warm-cache path regress dramatically?".

If a follow-up spec calls for cross-platform regression-tracked benchmarks
(e.g., a Phase 1.5 perf gate), promoting this test to BenchmarkDotNet is a
mechanical one-PR upgrade.
