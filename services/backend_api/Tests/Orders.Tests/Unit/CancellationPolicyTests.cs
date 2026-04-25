using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Orders.Tests.Unit;

/// <summary>
/// SC-004 — cancellation correctness matrix across (payment_state × shipment_exists × time-since-placed).
/// Uses the EF Core in-memory provider to exercise the policy lookup path without spinning up
/// Postgres. Note: in-memory provider doesn't enforce check constraints, but the policy logic
/// itself is what we're verifying here.
/// </summary>
public sealed class CancellationPolicyTests
{
    private static OrdersDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrdersDbContext(options);
    }

    [Fact]
    public async Task ShipmentExists_AlwaysDenies()
    {
        await using var db = NewContext(Guid.NewGuid().ToString());
        var policy = new CancellationPolicy(db);
        var decision = await policy.EvaluateAsync(
            "KSA", PaymentSm.Authorized, DateTimeOffset.UtcNow.AddMinutes(-1), shipmentExists: true,
            DateTimeOffset.UtcNow, CancellationToken.None);
        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("order.cancel.shipment_exists");
    }

    [Fact]
    public async Task Authorized_NoShipment_AlwaysAllowed()
    {
        await using var db = NewContext(Guid.NewGuid().ToString());
        var policy = new CancellationPolicy(db);
        var decision = await policy.EvaluateAsync(
            "KSA", PaymentSm.Authorized, DateTimeOffset.UtcNow.AddDays(-30), shipmentExists: false,
            DateTimeOffset.UtcNow, CancellationToken.None);
        decision.Allowed.Should().BeTrue();
        decision.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task PendingCod_NoShipment_Allowed()
    {
        await using var db = NewContext(Guid.NewGuid().ToString());
        var policy = new CancellationPolicy(db);
        var decision = await policy.EvaluateAsync(
            "KSA", PaymentSm.PendingCod, DateTimeOffset.UtcNow.AddHours(-2), false,
            DateTimeOffset.UtcNow, CancellationToken.None);
        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Captured_WithinDefaultWindow_Allowed()
    {
        await using var db = NewContext(Guid.NewGuid().ToString());
        var policy = new CancellationPolicy(db);
        // 24h default window; 12h since placed → allowed.
        var decision = await policy.EvaluateAsync(
            "KSA", PaymentSm.Captured, DateTimeOffset.UtcNow.AddHours(-12), false,
            DateTimeOffset.UtcNow, CancellationToken.None);
        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Captured_OutsideDefaultWindow_Denied()
    {
        await using var db = NewContext(Guid.NewGuid().ToString());
        var policy = new CancellationPolicy(db);
        var decision = await policy.EvaluateAsync(
            "KSA", PaymentSm.Captured, DateTimeOffset.UtcNow.AddHours(-48), false,
            DateTimeOffset.UtcNow, CancellationToken.None);
        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("order.cancel.window_expired");
    }

    [Fact]
    public async Task Captured_RespectsPerMarketPolicyOverride()
    {
        await using var db = NewContext(Guid.NewGuid().ToString());
        // Tighter EG policy: 6h capture window.
        db.CancellationPolicies.Add(new CancellationPolicyRow
        {
            MarketCode = "EG",
            AuthorizedCancelAllowed = true,
            CapturedCancelHours = 6,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var policy = new CancellationPolicy(db);
        // 4h since placed — within EG's 6h window.
        (await policy.EvaluateAsync("EG", PaymentSm.Captured,
            DateTimeOffset.UtcNow.AddHours(-4), false, DateTimeOffset.UtcNow, CancellationToken.None))
            .Allowed.Should().BeTrue();
        // 8h since placed — outside EG's 6h window.
        (await policy.EvaluateAsync("EG", PaymentSm.Captured,
            DateTimeOffset.UtcNow.AddHours(-8), false, DateTimeOffset.UtcNow, CancellationToken.None))
            .Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_AlwaysDenied()
    {
        await using var db = NewContext(Guid.NewGuid().ToString());
        var policy = new CancellationPolicy(db);
        var decision = await policy.EvaluateAsync(
            "KSA", PaymentSm.Failed, DateTimeOffset.UtcNow, false, DateTimeOffset.UtcNow, CancellationToken.None);
        decision.Allowed.Should().BeFalse();
    }
}
