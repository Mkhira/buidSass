using System.Diagnostics;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Benchmarks;

/// <summary>
/// Spec 020 T082 / T119 — eligibility-query latency lock per research §R1
/// (SC-004): warm-cache p95 ≤ 5 ms, p99 ≤ 15 ms when answering "may this
/// customer purchase this restricted SKU now?".
///
/// <para><b>Implementation note.</b> The original task called for
/// BenchmarkDotNet. The repo does not currently take a BenchmarkDotNet
/// dependency, and the per-task budget framing
/// (<c>"may relax in CI environments per project convention"</c>) makes
/// a regular xunit wall-clock check a reasonable substitute that doesn't
/// drag in a new test framework. This test:</para>
/// <list type="bullet">
///   <item>seeds a single eligibility-cache row for the (customer, market) pair;</item>
///   <item>warms the connection pool + EF metadata cache with a throw-away call;</item>
///   <item>runs N=200 evaluations of <see cref="ICustomerVerificationEligibilityQuery.EvaluateAsync"/>;</item>
///   <item>computes p50/p95/p99 from the resulting wall-clock samples;</item>
///   <item>asserts a generous CI-relaxed envelope of p95 ≤ 50 ms and p99 ≤ 150 ms
///         (10× the production budget per the project convention noted in
///         tasks.md). The numbers are recorded in
///         <c>baselines.md</c> alongside the run for spec
///         T119's perf-verification artifact.</item>
/// </list>
/// </summary>
public sealed class EligibilityBench : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_eligibility_bench")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private VerificationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<VerificationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new VerificationDbContext(options);
    }

    [Fact]
    public async Task Warm_cache_eligibility_query_meets_relaxed_p95_p99_envelope()
    {
        var customerId = Guid.NewGuid();
        var marketCode = "ksa";
        var sku = "DENT-RESTRICTED-001";

        // Seed the eligibility cache so the EvaluateAsync path is warm-cache only.
        await using (var seed = NewContext())
        {
            seed.EligibilityCache.Add(new VerificationEligibilityCache
            {
                CustomerId = customerId,
                MarketCode = marketCode,
                EligibilityClass = "eligible",
                ReasonCode = EligibilityReasonCode.Eligible.ToIcuKey(),
                ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
                ProfessionsJson = "[\"dentist\"]",
                ComputedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var policy = new RestrictedPolicy(sku, marketCode);

        await using var queryCtx = NewContext();
        var query = new CustomerVerificationEligibilityQuery(queryCtx, policy);

        // Warm-up: prime EF model metadata + Npgsql connection pool.
        for (int i = 0; i < 5; i++)
        {
            await query.EvaluateAsync(customerId, marketCode, sku, CancellationToken.None);
        }

        // Measure.
        const int sampleCount = 200;
        var samples = new long[sampleCount];
        var sw = new Stopwatch();
        for (int i = 0; i < sampleCount; i++)
        {
            sw.Restart();
            var result = await query.EvaluateAsync(customerId, marketCode, sku, CancellationToken.None);
            sw.Stop();
            samples[i] = sw.ElapsedTicks;
            result.Class.Should().Be(EligibilityClass.Eligible);
        }

        Array.Sort(samples);
        var p50 = TicksToMs(samples[sampleCount / 2]);
        var p95 = TicksToMs(samples[(int)(sampleCount * 0.95)]);
        var p99 = TicksToMs(samples[(int)(sampleCount * 0.99)]);

        // Relaxed CI envelope per the task spec — production lock is
        // p95 ≤ 5 ms, p99 ≤ 15 ms; CI gets 10× headroom.
        p95.Should().BeLessThan(50.0,
            "warm-cache eligibility p95 — relaxed CI envelope; production budget locked at 5 ms");
        p99.Should().BeLessThan(150.0,
            "warm-cache eligibility p99 — relaxed CI envelope; production budget locked at 15 ms");

        // Surface the sample distribution to the test log so T119's perf-
        // verification baseline-recording step can copy it into baselines.md.
        Console.WriteLine($"[EligibilityBench] n={sampleCount} p50={p50:F2}ms p95={p95:F2}ms p99={p99:F2}ms");
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Test-only restriction policy that returns the test SKU as restricted in
    /// the test market. Avoids pulling in spec 005's catalog binding for the
    /// benchmark scope.
    /// </summary>
    private sealed class RestrictedPolicy(string sku, string market) : IProductRestrictionPolicy
    {
        public ValueTask<ProductRestrictionPolicy> GetForSkuAsync(string s, CancellationToken ct) =>
            ValueTask.FromResult(new ProductRestrictionPolicy(
                Sku: s,
                RestrictedInMarkets: s == sku
                    ? new HashSet<string>(StringComparer.Ordinal) { market }
                    : new HashSet<string>(StringComparer.Ordinal),
                RequiredProfession: "dentist",
                VendorId: null));
    }
}
