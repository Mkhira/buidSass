using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BackendApi.Modules.Pricing.Admin.BusinessPricing;
using BackendApi.Modules.Pricing.Authorization;
using FluentAssertions;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.BusinessPricing;

/// <summary>
/// Spec 007-b T084 / T085 — bulk-import preview/commit flow plus strict-header
/// rejection. Validates contract §4.3 / §4.4.
/// </summary>
[Collection("pricing-fixture")]
public sealed class BulkImportTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Preview_StrictHeader_RejectsTitleCase()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        // Title_Case header — must be rejected per research §R7.
        var csv = "Tier_Code,SKU,Net_Minor,Markets\ntier-2,00000000-0000-0000-0000-000000000001,5000,sa\n";

        var resp = await PostMultipartAsync(client, csv);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("business_pricing.csv.header_invalid");
    }

    [Fact]
    public async Task Preview_SnakeCaseHeader_AcceptsAndReturnsPreviewToken()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-2");
        var productId = Guid.NewGuid();

        var csv = $"tier_code,sku,net_minor,markets\ntier-2,{productId},5000,sa\n";

        var resp = await PostMultipartAsync(client, csv);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<BulkImportPreviewResponse>();
        body!.PreviewToken.Should().NotBeNullOrEmpty();
        body.PreviewTokenExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        body.WouldInsert.Should().ContainSingle();
        body.WouldInsert[0].Outcome.Should().Be("insert");
    }

    [Fact]
    public async Task Preview_Then_Commit_PersistsRowsAndReportsCounts()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var tierId = await PricingTestSeedHelper.CreateTierAsync(factory.Services, "tier-2");
        var productId = Guid.NewGuid();
        var csv = $"tier_code,sku,net_minor,markets\ntier-2,{productId},5000,sa\n";

        var preview = await PostMultipartAsync(client, csv);
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewBody = await preview.Content.ReadFromJsonAsync<BulkImportPreviewResponse>();

        var commit = await client.PostAsJsonAsync(
            "/v1/admin/commercial/business-pricing/bulk-import/commit",
            new BulkImportCommitRequest(previewBody!.PreviewToken, IdempotencyKey: "test-key"));

        commit.StatusCode.Should().Be(HttpStatusCode.OK);
        var commitBody = await commit.Content.ReadFromJsonAsync<BulkImportCommitResponse>();
        commitBody!.Inserted.Should().Be(1);
        commitBody.Updated.Should().Be(0);
    }

    [Fact]
    public async Task Commit_WithUnknownToken_Returns400()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/business-pricing/bulk-import/commit",
            new BulkImportCommitRequest("not-a-real-token", IdempotencyKey: "k"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("business_pricing.preview_token.expired");
    }

    [Fact]
    public async Task Preview_RejectsRowsWithUnknownTierCode()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.B2BAuthoring });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var productId = Guid.NewGuid();
        var csv = $"tier_code,sku,net_minor,markets\nghost-tier,{productId},5000,sa\n";

        var resp = await PostMultipartAsync(client, csv);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<BulkImportPreviewResponse>();
        body!.Rejected.Should().ContainSingle()
            .Which.Code.Should().Be("business_pricing.csv.row_invalid");
    }

    private static async Task<HttpResponseMessage> PostMultipartAsync(HttpClient client, string csv)
    {
        using var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(csv);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "import.csv");

        return await client.PostAsync(
            "/v1/admin/commercial/business-pricing/bulk-import/preview", content);
    }
}
