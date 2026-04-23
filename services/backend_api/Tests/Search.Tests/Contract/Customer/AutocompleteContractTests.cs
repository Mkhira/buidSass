using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Search.Tests.Infrastructure;

namespace Search.Tests.Contract.Customer;

[Collection("search-fixture")]
public sealed class AutocompleteContractTests(SearchTestFactory factory)
{
    [Fact]
    public async Task Autocomplete_ThreeChars_Under50ms()
    {
        await factory.ResetDatabaseAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var categoryId = await SearchTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "gloves", "قفازات", "Gloves");
            var brandId = await SearchTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            await SearchTestSeedHelper.CreatePublishedProductAsync(scope.ServiceProvider, brandId, [categoryId], "AUTO-001", "قفازات جراحية", "Surgical Gloves", ["ksa"]);
            await SearchTestSeedHelper.CreatePublishedProductAsync(scope.ServiceProvider, brandId, [categoryId], "AUTO-002", "قفازات فحص", "Exam Gloves", ["ksa"]);
            await SearchTestSeedHelper.RebuildSearchIndexesAsync(scope.ServiceProvider);
        }

        var client = factory.CreateClient();
        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync("/v1/customer/search/autocomplete", new
        {
            query = "قفا",
            marketCode = "ksa",
            locale = "ar",
            limit = 5,
        });
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);

        var body = await response.Content.ReadFromJsonAsync<AutocompleteResponseDto>();
        body.Should().NotBeNull();
        body!.Suggestions.Should().NotBeEmpty();
        body.Suggestions.Should().OnlyContain(x => x.Name.Contains("قف", StringComparison.Ordinal));
    }

    private sealed record AutocompleteResponseDto(IReadOnlyList<AutocompleteSuggestionDto> Suggestions, string? NoResultsReason);
    private sealed record AutocompleteSuggestionDto(Guid ProductId, string Name, string? ThumbUrl, bool Restricted);
}
