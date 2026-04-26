using BackendApi.Modules.Returns.Customer.SubmitReturn;
using FluentAssertions;

namespace Returns.Tests.Unit;

public class ResolveTaxRateBpTests
{
    [Theory]
    // 100×1 unit, no discount, 15 tax → 1500 bp
    [InlineData(100, 1, 0, 15, 1500)]
    // 100×2, discount 50, tax 7 → taxable 150, rate ≈4.67% = 467 bp
    [InlineData(100, 2, 50, 7, 467)]
    // 0 taxable → 0 bp
    [InlineData(100, 1, 100, 0, 0)]
    public void Reconstructs_rate_within_rounding(long unit, int qty, long disc, long tax, int expectedBp)
    {
        var bp = Endpoint.ResolveTaxRateBp(tax, unit, qty, disc);
        // CR Minor round 3: tighten to exact equality — ResolveTaxRateBp uses deterministic
        // half-up integer rounding so any drift indicates a regression worth catching.
        bp.Should().Be(expectedBp);
    }
}
