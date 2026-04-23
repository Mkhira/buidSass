using System.Text.Json;
using System.Text.Json.Serialization;
using BackendApi.Modules.Search.Primitives.Normalization;
using FluentAssertions;

namespace Search.Tests.Unit;

public sealed class ArabicCoverageTests
{
    [Fact]
    public void ArabicGoldSet_NormalizationCoverage_IsAtLeast99Percent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "ar-gold.jsonl");
        File.Exists(path).Should().BeTrue("gold-standard Arabic dataset must be present");

        var normalizer = new ArabicNormalizer();
        var total = 0;
        var matched = 0;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var sample = JsonSerializer.Deserialize<ArabicGoldSample>(line);
            sample.Should().NotBeNull();

            total++;
            if (normalizer.Normalize(sample!.Query) == sample.Expected)
            {
                matched++;
            }
        }

        total.Should().BeGreaterOrEqualTo(500);
        var coverage = total == 0 ? 0d : (double)matched / total;
        coverage.Should().BeGreaterOrEqualTo(0.99d);
    }

    private sealed record ArabicGoldSample(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("expected")] string Expected);
}
