using BackendApi.Modules.Returns.Primitives;
using FluentAssertions;

namespace Returns.Tests.Unit;

public class RefundAmountCalculatorTests
{
    private readonly RefundAmountCalculator _calc = new();

    [Fact]
    public void Single_line_no_discount_15pct_tax()
    {
        var result = _calc.Compute(new[]
        {
            new RefundLineInput(Guid.NewGuid(), Guid.NewGuid(), OriginalQty: 1, QtyToRefund: 1,
                UnitPriceMinor: 100_00, OriginalDiscountMinor: 0, OriginalTaxMinor: 15_00, TaxRateBp: 1_500),
        }, restockingFeeMinor: 0);

        result.SubtotalMinor.Should().Be(100_00);
        result.DiscountMinor.Should().Be(0);
        result.TaxMinor.Should().Be(15_00);
        result.GrandRefundMinor.Should().Be(115_00);
    }

    [Fact]
    public void Pro_rates_discount_and_tax_by_qty_ratio()
    {
        var result = _calc.Compute(new[]
        {
            // OriginalQty=4 with discount=12, originalTax=2000, refund qty=2 →
            // discount portion = 12*2/4 = 6; tax portion = 2000*2/4 = 1000.
            new RefundLineInput(Guid.NewGuid(), Guid.NewGuid(), OriginalQty: 4, QtyToRefund: 2,
                UnitPriceMinor: 50_00, OriginalDiscountMinor: 12, OriginalTaxMinor: 2000, TaxRateBp: 1_500),
        }, restockingFeeMinor: 0);
        result.DiscountMinor.Should().Be(6);
        result.SubtotalMinor.Should().Be(100_00);
        result.TaxMinor.Should().Be(1000);
        result.GrandRefundMinor.Should().Be(100_00 - 6 + 1000);
    }

    [Fact]
    public void Restocking_fee_reduces_grand()
    {
        var result = _calc.Compute(new[]
        {
            new RefundLineInput(Guid.NewGuid(), Guid.NewGuid(), 1, 1, 100_00, 0, 0, 0),
        }, restockingFeeMinor: 10_00);
        result.GrandRefundMinor.Should().Be(90_00);
        result.RestockingFeeMinor.Should().Be(10_00);
    }

    [Fact]
    public void Restocking_fee_exceeds_refundable_throws()
    {
        var act = () => _calc.Compute(new[]
        {
            new RefundLineInput(Guid.NewGuid(), Guid.NewGuid(), 1, 1, 100_00, 0, 0, 0),
        }, restockingFeeMinor: 200_00);
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(-1)]
    public void Negative_restocking_fee_rejected(long fee)
    {
        var act = () => _calc.Compute(new[]
        {
            new RefundLineInput(Guid.NewGuid(), Guid.NewGuid(), 1, 1, 100, 0, 0, 0),
        }, restockingFeeMinor: fee);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SC_003_thousand_cases_match_explicit_formula()
    {
        // For 1000 deterministic cases the calculator must equal:
        // subtotal = unit*refQty
        // discount = origDisc * refQty / origQty   (floor)
        // tax = origTax * refQty / origQty         (floor)
        // grand = subtotal - discount + tax
        // This is the IDENTICAL pro-rata formula spec 012's IssueCreditNoteHandler uses,
        // so SC-009 reconciliation holds to 0 minor units.
        var rng = new Random(12345);
        for (int i = 0; i < 1000; i++)
        {
            var origQty = rng.Next(1, 11);
            var refQty = rng.Next(1, origQty + 1);
            var unit = rng.Next(50, 50_000);
            var origDisc = rng.Next(0, unit / 4) * origQty;
            var taxableBase = (long)unit * origQty - origDisc;
            var rateBp = rng.Next(0, 5_001);
            var origTax = taxableBase * rateBp / 10_000;
            var line = new RefundLineInput(Guid.NewGuid(), Guid.NewGuid(), origQty, refQty,
                unit, origDisc, origTax, rateBp);
            var result = _calc.Compute(new[] { line }, restockingFeeMinor: 0);
            var subtotal = (long)unit * refQty;
            var discount = (long)origDisc * refQty / origQty;
            var tax = origTax * refQty / origQty;
            var expectedGrand = subtotal - discount + tax;
            result.GrandRefundMinor.Should().Be(expectedGrand,
                $"case {i}: unit={unit} origQty={origQty} refQty={refQty} origDisc={origDisc} origTax={origTax}");
        }
    }
}
