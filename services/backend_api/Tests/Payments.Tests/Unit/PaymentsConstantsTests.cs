using BackendApi.Modules.Payments.Primitives;
using FluentAssertions;

namespace BackendApi.Tests.Payments.Unit;

/// <summary>V-7 — market eligibility matrix.</summary>
public sealed class PaymentsConstantsTests
{
    [Theory]
    [InlineData("mada", "sa", true)]
    [InlineData("mada", "eg", false)]
    [InlineData("meeza", "eg", true)]
    [InlineData("meeza", "sa", false)]
    [InlineData("bnpl_tabby", "sa", true)]
    [InlineData("bnpl_tabby", "eg", false)]
    [InlineData("bnpl_tamara", "sa", true)]
    [InlineData("bnpl_tamara", "eg", false)]
    [InlineData("bnpl_valu", "eg", true)]
    [InlineData("bnpl_valu", "sa", false)]
    [InlineData("apple_pay", "sa", true)]
    [InlineData("apple_pay", "eg", true)]
    [InlineData("stc_pay", "sa", true)]
    [InlineData("stc_pay", "eg", false)]
    [InlineData("cod", "sa", true)]
    [InlineData("cod", "eg", true)]
    [InlineData("bank_transfer", "sa", true)]
    [InlineData("bank_transfer", "eg", true)]
    public void V7_MarketEligibility_MatchesContract(string method, string market, bool expected)
    {
        PaymentsConstants.Methods.EligibleForMarket(method, market).Should().Be(expected);
    }

    [Theory]
    [InlineData("hyperpay", "sa", true)]
    [InlineData("hyperpay", "eg", false)]
    [InlineData("paymob", "eg", true)]
    [InlineData("paymob", "sa", false)]
    [InlineData("tap", "sa", true)]
    [InlineData("kashier", "eg", true)]
    [InlineData("tabby", "sa", true)]
    [InlineData("tamara", "sa", true)]
    [InlineData("valu", "eg", true)]
    public void Provider_MarketSupport_MatchesAdr007(string provider, string market, bool expected)
    {
        PaymentsConstants.Providers.SupportsMarket(provider, market).Should().Be(expected);
    }

    [Theory]
    [InlineData("sa", "SAR")]
    [InlineData("eg", "EGP")]
    public void CurrencyForMarket_MatchesMarket(string market, string expected)
    {
        PaymentsConstants.Currencies.For(market).Should().Be(expected);
    }
}
