using BackendApi.Modules.Pricing.Primitives.Rounding;
using FluentAssertions;

namespace Pricing.Tests.Unit;

public sealed class BankersRoundingTests
{
    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 2)]
    [InlineData(3.5, 4)]
    [InlineData(-0.5, 0)]
    [InlineData(-1.5, -2)]
    [InlineData(-2.5, -2)]
    [InlineData(0.4, 0)]
    [InlineData(0.6, 1)]
    [InlineData(100.0, 100)]
    [InlineData(99.5, 100)]
    [InlineData(100.5, 100)]
    public void RoundMinor_HalfEven(double value, long expected)
    {
        BankersRounding.RoundMinor((decimal)value).Should().Be(expected);
    }

    [Fact]
    public void RoundMinor_NoDriftAcross20Lines()
    {
        decimal perLine = 0.5m;
        long total = 0;
        for (var i = 0; i < 20; i++)
        {
            total += BankersRounding.RoundMinor(perLine);
        }
        // 20 × 0.5 rounded half-even → alternates 0,0 in pairs → 0 total if starting at 0.5
        total.Should().Be(0);
    }
}
