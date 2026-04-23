using BackendApi.Modules.Pricing.Primitives;
using BackendApi.Modules.Pricing.Primitives.Layers;
using FluentAssertions;

namespace Pricing.Tests.Unit.Layers;

public sealed class ListPriceLayerTests
{
    [Fact]
    public void Apply_SeedsNetWithListTimesQty()
    {
        var productId = Guid.NewGuid();
        var ctx = NewCtx();
        var ws = new PricingWorkingSet(ctx, new[]
        {
            new WorkingLine(productId, 3, 10_000, false, Array.Empty<Guid>()),
        });

        new ListPriceLayer().Apply(ws);

        ws.Lines[0].NetMinor.Should().Be(30_000);
        ws.Lines[0].Explanation.Should().ContainSingle(e => e.Layer == "list" && e.AppliedMinor == 30_000);
    }

    private static PricingContext NewCtx() => new(
        MarketCode: "ksa",
        Locale: "en",
        Account: null,
        Lines: Array.Empty<PricingContextLine>(),
        CouponCode: null,
        QuotationId: null,
        OrderId: null,
        NowUtc: DateTimeOffset.UtcNow,
        Mode: PricingMode.Preview);
}
