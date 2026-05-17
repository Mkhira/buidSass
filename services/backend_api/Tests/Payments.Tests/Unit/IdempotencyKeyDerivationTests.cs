using BackendApi.Modules.Payments.Services;
using FluentAssertions;

namespace BackendApi.Tests.Payments.Unit;

/// <summary>T018 + AC-9 — V-2 idempotency-key derivation.</summary>
public sealed class IdempotencyKeyDerivationTests
{
    [Fact]
    public void SameInputs_ProduceSameKey()
    {
        var orderId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var k1 = IdempotencyKeyDerivation.Derive(orderId, "mada", attemptId);
        var k2 = IdempotencyKeyDerivation.Derive(orderId, "mada", attemptId);
        k1.Should().Be(k2);
        k1.Should().HaveLength(64); // sha256 hex
    }

    [Fact]
    public void DifferentAttemptIds_ProduceDifferentKeys()
    {
        var orderId = Guid.NewGuid();
        var k1 = IdempotencyKeyDerivation.Derive(orderId, "mada", Guid.NewGuid());
        var k2 = IdempotencyKeyDerivation.Derive(orderId, "mada", Guid.NewGuid());
        k1.Should().NotBe(k2);
    }

    [Fact]
    public void DifferentMethods_ProduceDifferentKeys()
    {
        var orderId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var k1 = IdempotencyKeyDerivation.Derive(orderId, "mada", attemptId);
        var k2 = IdempotencyKeyDerivation.Derive(orderId, "card", attemptId);
        k1.Should().NotBe(k2);
    }
}
