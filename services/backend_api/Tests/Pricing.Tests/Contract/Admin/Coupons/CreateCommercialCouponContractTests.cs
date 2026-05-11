using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using FluentAssertions;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Contract.Admin.Coupons;

/// <summary>
/// Spec 007-b T050 — contract-shape conformance for
/// <c>POST /v1/admin/commercial/coupons</c>. Covers contract §2.1 — the
/// owned reason codes <c>coupon.code.duplicate</c>,
/// <c>coupon.label.required_bilingual</c>, <c>coupon.schedule.invalid_window</c>,
/// <c>coupon.value.out_of_range</c>, and <c>coupon.markets.required</c>.
///
/// Contract tests differ from integration tests by anchoring on the
/// problem+json envelope shape (<c>type</c> URI, <c>reasonCode</c> extension,
/// <c>status</c> field) so any future renaming of the reason code without
/// updating ICU keys / consumer SDKs is caught here, before integration tests
/// notice via behavioral drift.
/// </summary>
[Collection("pricing-fixture")]
public sealed class CreateCommercialCouponContractTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Created_Response_Carries_State_Id_RowVersion()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons", BuildBaseRequest("CONTRACT01"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.TryGetProperty("id", out _).Should().BeTrue();
        root.TryGetProperty("state", out _).Should().BeTrue();
        root.TryGetProperty("rowVersion", out _).Should().BeTrue();
        root.GetProperty("state").GetString().Should().Be("draft");
    }

    [Theory]
    [InlineData("CONTRACT_DUP", "coupon.code.duplicate", "duplicate-code")]
    [InlineData("CONTRACT_BIL", "coupon.label.required_bilingual", "missing-en")]
    [InlineData("CONTRACT_WIN", "coupon.schedule.invalid_window", "invalid-window")]
    [InlineData("CONTRACT_VAL", "coupon.value", "value-out-of-range")]
    [InlineData("CONTRACT_MKT", "coupon.markets.required", "no-markets")]
    public async Task Error_Bodies_Carry_Stable_ReasonCode(string code, string expectedReason, string scenario)
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        // First request seeds the duplicate-code scenario; ignore its outcome.
        if (scenario == "duplicate-code")
        {
            await client.PostAsJsonAsync("/v1/admin/commercial/coupons", BuildBaseRequest(code));
        }

        var req = scenario switch
        {
            "missing-en" => BuildBaseRequest(code) with { Label = new("خصم", "") },
            "invalid-window" => BuildBaseRequest(code) with
            {
                ValidFrom = DateTimeOffset.UtcNow.AddDays(5),
                ValidTo = DateTimeOffset.UtcNow.AddDays(1),
            },
            "value-out-of-range" => BuildBaseRequest(code) with { Type = "percent_off", Value = 0 },
            "no-markets" => BuildBaseRequest(code) with { Markets = Array.Empty<string>() },
            _ => BuildBaseRequest(code),
        };

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/coupons", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Problem+json envelope conformance
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(400);
        root.GetProperty("type").GetString().Should().StartWith("https://errors.dental-commerce/commercial/");
        root.GetProperty("reasonCode").GetString()!.Should().Contain(expectedReason);
    }

    private static CreateCommercialCouponRequest BuildBaseRequest(string code) => new(
        Code: code,
        Markets: new[] { "ksa" },
        Type: "percent_off",
        Value: 5,
        AmountOffMinor: null,
        CapMinor: 1_000,
        PerCustomerLimit: 1,
        OverallLimit: 100,
        ExcludesRestricted: false,
        ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
        ValidTo: DateTimeOffset.UtcNow.AddDays(7),
        StacksWithPromotions: true,
        DisplayInBanners: false,
        Label: new("خصم تجريبي", "Test discount"),
        Description: null);
}
