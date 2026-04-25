using BackendApi.Modules.Checkout.Primitives;
using FluentAssertions;

namespace Checkout.Tests.Unit;

public sealed class PaymentMethodCatalogTests
{
    private static readonly PaymentMethodCatalog Catalog = new();

    [Theory]
    [InlineData("ksa", "mada", true)]
    [InlineData("ksa", "stc_pay", true)]
    [InlineData("ksa", "bnpl", false)]     // bnpl is EG-only per launch config
    [InlineData("eg", "bnpl", true)]
    [InlineData("eg", "stc_pay", false)]
    [InlineData("unknown", "card", false)]
    public void IsMethodAllowed_HonoursPerMarketConfig(string market, string method, bool expected)
    {
        Catalog.IsMethodAllowed(market, method).Should().Be(expected);
    }

    [Fact]
    public void CheckCod_UnderCap_Allowed()
    {
        var result = Catalog.CheckCod("ksa", totalMinor: 1_000_00, cartHasRestricted: false);
        result.Allowed.Should().BeTrue();
        result.ReasonCode.Should().BeNull();
    }

    [Fact]
    public void CheckCod_OverCap_BlockedWithReason()
    {
        // KSA cap per R9 default is 2000 SAR = 200000 minor.
        var result = Catalog.CheckCod("ksa", totalMinor: 300_00_00L, cartHasRestricted: false);
        result.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("checkout.cod_cap_exceeded");
    }

    [Fact]
    public void CheckCod_RestrictedProduct_BlockedByPolicy()
    {
        var result = Catalog.CheckCod("ksa", totalMinor: 1_000_00, cartHasRestricted: true);
        result.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("checkout.cod_restricted_product");
    }

    [Fact]
    public void CheckCod_UnknownMarket_Rejects()
    {
        var result = Catalog.CheckCod("xy", totalMinor: 100, cartHasRestricted: false);
        result.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("cart.payment.cod_not_available");
    }

    [Fact]
    public void AllowedMethods_Ksa_IncludesExpectedLaunchMethods()
    {
        var methods = Catalog.AllowedMethods("ksa");
        methods.Should().Contain(new[]
        {
            PaymentMethodCatalog.Card,
            PaymentMethodCatalog.Mada,
            PaymentMethodCatalog.ApplePay,
            PaymentMethodCatalog.StcPay,
            PaymentMethodCatalog.BankTransfer,
            PaymentMethodCatalog.Cod,
        });
    }
}
