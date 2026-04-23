using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Search.Tests.Infrastructure;

namespace Search.Tests.Contract.Admin;

[Collection("search-fixture")]
public sealed class ReindexContractTests(SearchTestFactory factory)
{
    [Fact]
    public async Task Reindex_Started_ReturnsJobAndStream()
    {
        await factory.ResetDatabaseAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "reindex", "اعادة", "Reindex");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                "REINDEX-001",
                "منتج اعادة",
                "Reindex Product",
                ["ksa"]);
        }

        var token = await SearchAdminAuthHelper.IssueAdminTokenAsync(factory, ["search.reindex", "search.read"]);

        var client = factory.CreateClient();
        SearchAdminAuthHelper.SetBearer(client, token);

        var start = await client.PostAsync("/v1/admin/search/reindex?index=products-ksa-en", content: null);
        start.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var startJson = await start.Content.ReadFromJsonAsync<ReindexStartResponseDto>();
        startJson.Should().NotBeNull();
        startJson!.JobId.Should().NotBe(Guid.Empty);
        startJson.StreamPath.Should().NotBeNullOrWhiteSpace();

        using var streamRequest = new HttpRequestMessage(HttpMethod.Get, startJson.StreamPath);
        using var streamResponse = await client.SendAsync(
            streamRequest,
            HttpCompletionOption.ResponseHeadersRead);

        streamResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        streamResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        await using var body = await streamResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(body);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        var sawEvent = false;
        while (!timeout.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                sawEvent = true;
                break;
            }
        }

        sawEvent.Should().BeTrue("SSE stream should emit at least one event");
    }

    [Fact]
    public async Task Reindex_Concurrent_Returns409()
    {
        await factory.ResetDatabaseAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "reindex2", "اعادة٢", "Reindex2");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            await SearchTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider,
                brandId,
                [categoryId],
                "REINDEX-002",
                "منتج اعادة ٢",
                "Reindex Product 2",
                ["ksa"]);
        }

        var token = await SearchAdminAuthHelper.IssueAdminTokenAsync(factory, ["search.reindex", "search.read"]);

        var client = factory.CreateClient();
        SearchAdminAuthHelper.SetBearer(client, token);

        var first = await client.PostAsync("/v1/admin/search/reindex?index=products-ksa-en", content: null);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var second = await client.PostAsync("/v1/admin/search/reindex?index=products-ksa-en", content: null);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var json = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
        reasonCode.GetString().Should().Be("search.reindex.in_progress");
        json.RootElement.TryGetProperty("activeJobId", out var activeJobId).Should().BeTrue();
        Guid.TryParse(activeJobId.GetString(), out _).Should().BeTrue();
    }

    private sealed record ReindexStartResponseDto(Guid JobId, string StreamPath);
}
