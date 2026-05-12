using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Pricing.Admin.Campaigns;
using BackendApi.Modules.Pricing.Authorization;
using FluentAssertions;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Contract.Admin.Campaigns;

/// <summary>
/// Spec 007-b T093 — contract-shape conformance for
/// <c>POST /v1/admin/commercial/campaigns</c>. Pins the problem+json envelope
/// (<c>status</c>, <c>type</c>, <c>reasonCode</c>) and the owned reason codes
/// for the 4 US4 acceptance scenarios so consumer SDKs and ICU keys don't drift
/// silently.
/// </summary>
[Collection("pricing-fixture")]
public sealed class CreateCampaignContractTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Created_Response_Carries_State_Id_RowVersion()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/campaigns",
            BuildBaseRequest("Contract test campaign"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.TryGetProperty("id", out _).Should().BeTrue();
        root.TryGetProperty("state", out _).Should().BeTrue();
        root.TryGetProperty("row_version", out _).Should().BeTrue();
        root.GetProperty("state").GetString().Should().Be("draft");
    }

    [Theory]
    [InlineData("missing-bilingual", "campaign.name.required_bilingual")]
    [InlineData("invalid-window", "campaign.schedule.invalid_window")]
    [InlineData("missing-markets", "campaign.markets.empty_or_invalid")]
    [InlineData("invalid-link-kind", "campaign.link.invalid_kind")]
    public async Task Error_Bodies_Carry_Stable_ReasonCode(string scenario, string expectedReason)
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();

        var req = scenario switch
        {
            "missing-bilingual" => BuildBaseRequest("ok") with { Name = new CampaignBilingual("بدون انكليزي", "") },
            "invalid-window" => BuildBaseRequest("ok") with
            {
                ValidFrom = DateTimeOffset.UtcNow.AddDays(5),
                ValidTo = DateTimeOffset.UtcNow.AddDays(1),
            },
            "missing-markets" => BuildBaseRequest("ok") with { Markets = Array.Empty<string>() },
            "invalid-link-kind" => BuildBaseRequest("ok") with
            {
                CampaignLink = new CampaignLinkRequest("bogus_kind", null),
            },
            _ => BuildBaseRequest("ok"),
        };

        var resp = await client.PostAsJsonAsync("/v1/admin/commercial/campaigns", req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(400);
        root.GetProperty("type").GetString().Should().StartWith("https://errors.dental-commerce/commercial/");
        root.GetProperty("reasonCode").GetString().Should().Be(expectedReason);
    }

    private async Task<HttpClient> BuildOperatorClientAsync()
    {
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);
        return client;
    }

    private static CreateCampaignRequest BuildBaseRequest(string nameEn) => new(
        Name: new CampaignBilingual("حملة اختبار", nameEn),
        ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
        ValidTo: DateTimeOffset.UtcNow.AddDays(30),
        Markets: new[] { "sa" },
        LandingQuery: null,
        NotesInternal: null,
        CampaignLink: null);
}
