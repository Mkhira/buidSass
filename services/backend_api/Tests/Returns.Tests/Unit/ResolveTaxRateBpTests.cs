using BackendApi.Modules.Returns.Customer.SubmitReturn;
using FluentAssertions;

namespace Returns.Tests.Unit;

public class ResolveTaxRateBpTests
{
    [Theory]
    // 100×1 unit, no discount, 15 tax → 1500 bp
    [InlineData(100, 1, 0, 15, 1500)]
    // 100×2, discount 50, tax 7.5 → taxable 150, rate 5% = 500 bp
    [InlineData(100, 2, 50, 7, 467)]
    // 0 taxable → 0 bp
    [InlineData(100, 1, 100, 0, 0)]
    public void Reconstructs_rate_within_rounding(long unit, int qty, long disc, long tax, int expectedBp)
    {
        var bp = Endpoint.ResolveTaxRateBp(tax, unit, qty, disc);
        bp.Should().BeInRange(expectedBp - 1, expectedBp + 1);
    }
}
