using BackendApi.Modules.Payments.Providers;
using BackendApi.Modules.Payments.Providers.HyperPay;
using BackendApi.Modules.Payments.Providers.Kashier;
using BackendApi.Modules.Payments.Providers.Paymob;
using BackendApi.Modules.Payments.Providers.Tabby;
using BackendApi.Modules.Payments.Providers.Tamara;
using BackendApi.Modules.Payments.Providers.Tap;
using BackendApi.Modules.Payments.Providers.Valu;
using FluentAssertions;

namespace BackendApi.Tests.Payments.Unit;

/// <summary>
/// Pin the per-provider capability matrix from research §4 and §8. Drift in
/// these values would silently change the daily reconciliation routing and
/// the webhook-replay UI surface, so they need test coverage.
/// </summary>
public sealed class ProviderCapabilityMatrixTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    public static IEnumerable<object[]> Cases()
    {
        // (provider, market, supports_method_card_or_bnpl, supports_ledger_api, supports_webhook_replay)
        yield return new object[] { new HyperPayProvider(Clock), "sa", "mada", true, true };
        yield return new object[] { new TapProvider(Clock), "sa", "card", true, true };
        yield return new object[] { new PaymobProvider(Clock), "eg", "card", true, true };
        yield return new object[] { new KashierProvider(Clock), "eg", "card", false, true };
        yield return new object[] { new TabbyProvider(Clock), "sa", "bnpl_tabby", true, true };
        yield return new object[] { new TamaraProvider(Clock), "sa", "bnpl_tamara", true, true };
        yield return new object[] { new ValuProvider(Clock), "eg", "bnpl_valu", false, false };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Provider_MatchesResearchExpectations(
        IPaymentProvider provider, string market, string method, bool supportsLedger, bool supportsReplay)
    {
        provider.SupportsMarket(market).Should().BeTrue();
        provider.SupportsMethod(method).Should().BeTrue();
        provider.SupportsLedgerApi.Should().Be(supportsLedger);
        provider.SupportsWebhookReplay.Should().Be(supportsReplay);
    }

    [Fact]
    public async Task NonReplaySupportingProvider_ReportsNotSupported()
    {
        var valu = new ValuProvider(Clock);
        var result = await valu.ReplayWebhooksAsync(
            new DateRange(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow),
            CancellationToken.None);
        result.Supported.Should().BeFalse();
        result.NotSupportedReason.Should().NotBeNullOrWhiteSpace();
    }
}
