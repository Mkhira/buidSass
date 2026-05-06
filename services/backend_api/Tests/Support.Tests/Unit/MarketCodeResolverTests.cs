using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using BackendApi.Modules.Support.Primitives;
using FluentAssertions;

namespace Support.Tests.Unit;

/// <summary>
/// T049 — covers FR-006a:
///   (a) linked-entity present + Available → linked entity's market;
///   (b) linked-entity Unavailable → throws MarketCodeUnresolvableException;
///   (c) no linked entity → customer-of-record fallback.
/// Uses fakes from Modules/Shared/Testing/.
/// </summary>
public class MarketCodeResolverTests
{
    private MarketCodeResolver BuildResolver(
        FakeOrderLinkedReadContract? orders = null,
        FakeReturnLinkedReadContract? returns = null,
        FakeQuoteLinkedReadContract? quotes = null,
        FakeReviewLinkedReadContract? reviews = null,
        FakeVerificationLinkedReadContract? verifications = null)
    {
        return new MarketCodeResolver(
            orders ?? new FakeOrderLinkedReadContract(),
            returns ?? new FakeReturnLinkedReadContract(),
            quotes ?? new FakeQuoteLinkedReadContract(),
            reviews ?? new FakeReviewLinkedReadContract(),
            verifications ?? new FakeVerificationLinkedReadContract());
    }

    [Fact]
    public async Task NoLinkedEntity_FallsBackToCustomerOfRecord()
    {
        var resolver = BuildResolver();
        var customerId = Guid.NewGuid();

        var market = await resolver.ResolveAsync(
            linkedEntityKind: null,
            linkedEntityId: null,
            actorCustomerId: customerId,
            customerMarketOfRecord: "EG",
            ct: CancellationToken.None);

        market.MarketCode.Should().Be("EG");
        market.OwnedByActor.Should().BeTrue();
        market.VendorId.Should().BeNull();
    }

    [Fact]
    public async Task LinkedEntityAvailable_ReturnsLinkedMarket()
    {
        var customerId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();
        var orders = new FakeOrderLinkedReadContract();
        orders.Stage("order_line", new LinkedEntityReadResult(
            LinkedEntityId: orderLineId,
            MarketCode: "SA",
            OwnedByActor: true,
            VendorId: null,
            DisplaySummary: null));

        var resolver = BuildResolver(orders: orders);

        var market = await resolver.ResolveAsync(
            linkedEntityKind: "order_line",
            linkedEntityId: orderLineId,
            actorCustomerId: customerId,
            customerMarketOfRecord: "EG",
            ct: CancellationToken.None);

        market.MarketCode.Should().Be("SA");
        market.OwnedByActor.Should().BeTrue();
    }

    [Fact]
    public async Task LinkedEntityUnavailable_ThrowsMarketCodeUnresolvable()
    {
        var resolver = BuildResolver();
        var orderLineId = Guid.NewGuid();

        var act = async () => await resolver.ResolveAsync(
            linkedEntityKind: "order_line",
            linkedEntityId: orderLineId,
            actorCustomerId: Guid.NewGuid(),
            customerMarketOfRecord: "EG",
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<MarketCodeUnresolvableException>();
    }

    [Fact]
    public async Task LinkedEntity_NotOwnedFlag_PropagatesThrough()
    {
        var customerId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        var quotes = new FakeQuoteLinkedReadContract();
        quotes.Stage(new LinkedEntityReadResult(
            LinkedEntityId: quoteId,
            MarketCode: "SA",
            OwnedByActor: false,
            VendorId: null,
            DisplaySummary: null));

        var resolver = BuildResolver(quotes: quotes);

        var market = await resolver.ResolveAsync(
            linkedEntityKind: "quote",
            linkedEntityId: quoteId,
            actorCustomerId: customerId,
            customerMarketOfRecord: "SA",
            ct: CancellationToken.None);

        market.OwnedByActor.Should().BeFalse();
    }
}
