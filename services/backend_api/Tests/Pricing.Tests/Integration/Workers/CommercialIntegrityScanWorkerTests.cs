using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Pricing.Workers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Workers;

/// <summary>
/// Spec 007-b T149 — <see cref="CommercialIntegrityScanWorker"/> reports
/// malformed rows that bypass admin validators (e.g. were inserted via raw
/// SQL during a hotfix). SC-004: a clean DB produces zero violations; a row
/// inserted with <c>valid_to ≤ valid_from</c> shows up in the violation count.
/// </summary>
[Collection("pricing-fixture")]
public sealed class CommercialIntegrityScanWorkerTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Tick_OnCleanDb_ReportsZeroViolations()
    {
        await factory.ResetDatabaseAsync();

        var worker = new CommercialIntegrityScanWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<CommercialIntegrityScanWorker>.Instance);
        var violations = await worker.TickAsync(CancellationToken.None);
        violations.Should().Be(0, "fresh DB has no malformed rows");
    }

    [Fact]
    public async Task Tick_DetectsCouponWithInvertedWindow()
    {
        await factory.ResetDatabaseAsync();

        // Insert a malformed coupon by bypassing the admin validators via
        // direct EF — the validators would reject ValidTo <= ValidFrom.
        var nowUtc = DateTimeOffset.UtcNow;
        await using (var conn = new NpgsqlConnection(factory.ConnectionString))
        {
            await conn.OpenAsync();
            // Raw SQL with a deliberately inverted window. Bypasses EF entity
            // configuration (which would otherwise enforce some shape rules).
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO pricing.coupons
                  ("Id", "Code", "Kind", "Value", "AmountOffMinor", "CapMinor",
                   "PerCustomerLimit", "OverallLimit", "ExcludesRestricted",
                   "StacksWithPromotions", "MarketCodes", "ValidFrom", "ValidTo",
                   "DisplayInBanners", "LabelAr", "LabelEn", "State",
                   "StateChangedAtUtc", "StateChangedByActorId", "AuthorActorId",
                   "IsActive", "UsedCount", "CreatedAt", "UpdatedAt",
                   "AppliesToBroken")
                VALUES
                  (@p_id, 'BAD-WINDOW', 'percent', 5, NULL, 100000,
                   1, 100, false,
                   true, ARRAY['sa'], @p_now_plus_5d, @p_now_plus_1d,
                   false, 'بنافذة غير صحيحة', 'Bad window',
                   2,  /* LifecycleState.Active — enum-as-int (data-model §3.1) */
                   @p_now, @p_system_actor, @p_system_actor,
                   true, 0, @p_now, @p_now,
                   false);
                """;
            cmd.Parameters.AddWithValue("p_id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("p_now", nowUtc);
            cmd.Parameters.AddWithValue("p_now_plus_1d", nowUtc.AddDays(1));
            cmd.Parameters.AddWithValue("p_now_plus_5d", nowUtc.AddDays(5));
            cmd.Parameters.AddWithValue("p_system_actor", CommercialPermissions.SystemActorId);
            await cmd.ExecuteNonQueryAsync();
        }

        var worker = new CommercialIntegrityScanWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(nowUtc),
            NullLogger<CommercialIntegrityScanWorker>.Instance);
        var violations = await worker.TickAsync(CancellationToken.None);
        violations.Should().BeGreaterThan(0,
            "an active coupon with valid_to <= valid_from must surface as a violation");
    }
}
