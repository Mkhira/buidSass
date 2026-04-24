using BackendApi.Modules.Inventory.Primitives;
using FluentAssertions;
using Inventory.Tests.Infrastructure;

namespace Inventory.Tests.Unit;

[Collection("inventory-fixture")]
public sealed class AtsCalculatorTests
{
    [Fact]
    public void Compute_SubtractsReservedAndSafetyStock()
    {
        var sut = new AtsCalculator();

        var ats = sut.Compute(onHand: 10, reserved: 3, safetyStock: 2);

        ats.Should().Be(5);
    }

    [Fact]
    public void Compute_AllowsZeroResult()
    {
        var sut = new AtsCalculator();

        var ats = sut.Compute(onHand: 5, reserved: 3, safetyStock: 2);

        ats.Should().Be(0);
    }

    [Fact]
    public void Compute_CanBeNegativeWhenOverReserved()
    {
        var sut = new AtsCalculator();

        var ats = sut.Compute(onHand: 2, reserved: 3, safetyStock: 1);

        ats.Should().Be(-2);
    }
}
