using BackendApi.Modules.Payments.PciScope;
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
/// T007 + T063 + AC-35 — egress payload allow-list (BR-14 / V-8). Every
/// provider impl MUST satisfy the allow-list. Adding a forbidden key MUST
/// throw <see cref="EgressPayloadFilterViolation"/> immediately.
/// </summary>
public sealed class EgressPayloadFilterTests
{
    [Fact]
    public void AllowedKeys_Pass()
    {
        var keys = new[]
        {
            "payment_id", "order_id", "market_code", "method",
            "amount", "currency", "idempotency_key", "customer_ref",
            "recipient_name_redacted", "recipient_phone_masked_last4",
            "merchant_ref", "return_url", "webhook_url",
        };
        Action act = () => EgressPayloadFilter.EnsureCreatePaymentPayloadAllowed(keys, "test");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("pan")]
    [InlineData("card_number")]
    [InlineData("cvv")]
    [InlineData("national_id")]
    [InlineData("recipient_email")]
    [InlineData("date_of_birth")]
    public void ForbiddenKeys_Rejected(string forbidden)
    {
        var keys = new[] { "payment_id", "amount", forbidden };
        Action act = () => EgressPayloadFilter.EnsureCreatePaymentPayloadAllowed(keys, "test");
        act.Should().Throw<EgressPayloadFilterViolation>()
            .Which.ForbiddenKeys.Should().Contain(forbidden);
    }

    [Fact]
    public void AllProviders_SatisfyAllowList()
    {
        // T063 — every provider's CreatePayment egress projection MUST honor
        // the egress filter. We pass a method that each provider actually
        // supports so the call reaches the egress-projection path; using a
        // single hardcoded method ("card") would let unsupported providers
        // short-circuit before the filter runs and silently weaken the test.
        var clock = TimeProvider.System;
        var cases = new (IPaymentProvider Provider, string Market, string Method, string Currency)[]
        {
            (new HyperPayProvider(clock), "sa", "mada", "SAR"),
            (new TapProvider(clock), "sa", "card", "SAR"),
            (new PaymobProvider(clock), "eg", "card", "EGP"),
            (new KashierProvider(clock), "eg", "card", "EGP"),
            (new TabbyProvider(clock), "sa", "bnpl_tabby", "SAR"),
            (new TamaraProvider(clock), "sa", "bnpl_tamara", "SAR"),
            (new ValuProvider(clock), "eg", "bnpl_valu", "EGP"),
        };
        foreach (var (provider, market, method, currency) in cases)
        {
            var dispatch = new CreatePaymentDispatch(
                PaymentId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(),
                MarketCode: market,
                Method: method,
                Amount: 100m,
                Currency: currency,
                IdempotencyKey: new string('a', 64),
                CustomerRef: Guid.NewGuid().ToString("N"),
                RecipientNameRedacted: "A. K",
                RecipientPhoneMaskedLast4: "1234");
            var act = () => provider.CreatePaymentAsync(dispatch, CancellationToken.None).GetAwaiter().GetResult();
            act.Should().NotThrow($"provider {provider.ProviderId} must obey BR-14 egress allow-list");
        }
    }
}
