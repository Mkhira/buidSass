using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Search.Primitives.Normalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Integration.Filtering;

/// <summary>
/// Spec 022 T048 — wordlist coverage matrix per market, AR-normalization
/// corner cases (hamza, alef variants, ya/alif maqsura, diacritics, tatweel),
/// case-insensitive EN matching (SC-010).
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class ProfanityFilterCoverageMatrixTests : IAsyncLifetime
{
    private readonly ReviewsPostgresFixture _fx;

    public ProfanityFilterCoverageMatrixTests(ReviewsPostgresFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        // Seed a per-market matrix once. Terms are stored Arabic-normalized +
        // lowercased (matches what UpsertWordlistTerm does at write time).
        await using var db = _fx.NewContext();
        var nowUtc = DateTimeOffset.UtcNow;

        var seed = new[]
        {
            // EN — case-insensitive matching (term stored lowercased)
            ("SA", "fraud"),
            ("SA", "scam"),
            // EN multi-word
            ("SA", "fake review"),
            // AR — stored already-normalized via the same normalizer used at submission
            ("SA", new ArabicNormalizer().Normalize("احتيال")),  // "fraud" in AR
            ("EG", "spam"),
            ("EG", new ArabicNormalizer().Normalize("سيء")),     // "bad" in AR
        };

        foreach (var (market, term) in seed)
        {
            var exists = await db.Wordlists.AnyAsync(w => w.MarketCode == market && w.Term == term);
            if (!exists)
            {
                db.Wordlists.Add(new ReviewsFilterWordlist
                {
                    MarketCode = market,
                    Term = term,
                    CreatedByActorId = Guid.Empty,
                    CreatedAtUtc = nowUtc,
                });
            }
        }
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("This product is FRAUD",            true)]   // upper-case match
    [InlineData("totally legitimate purchase",      false)]
    [InlineData("Beware: SCAM alert!!",             true)]   // punctuation around match
    [InlineData("fake review writer needed",        true)]   // multi-word match
    [InlineData("a fake review submitted later",    true)]   // multi-word match mid-sentence
    [InlineData("",                                 false)]
    [InlineData("clean text",                       false)]
    public async Task Case_insensitive_EN_matching_works(string text, bool shouldTrip)
    {
        var filter = NewFilter();
        var result = await filter.EvaluateAsync("SA", text);
        result.Tripped.Should().Be(shouldTrip);
    }

    [Theory]
    [InlineData("هذا المنتج احتيال")]                  // exact AR
    [InlineData("هذا المنتج إحتيال")]                  // hamza variant (إ)
    [InlineData("هذا المنتج أحتيال")]                  // hamza variant (أ)
    [InlineData("هذا المنتج آحتيال")]                  // hamza-alef variant (آ)
    [InlineData("هذا المنتج احــــتيال")]               // tatweel between letters
    [InlineData("هذا المنتج احْتِيَالٌ")]                  // diacritics on letters
    public async Task Arabic_normalization_corner_cases_all_match_canonical_term(string text)
    {
        var filter = NewFilter();
        var result = await filter.EvaluateAsync("SA", text);
        result.Tripped.Should().BeTrue($"normalization must collapse '{text}' to the seeded canonical term");
    }

    [Fact]
    public async Task Per_market_isolation_a_term_seeded_for_one_market_does_not_trip_another()
    {
        // Seed a unique term ONLY on EG — must not trip on SA.
        const string egOnlyTerm = "egonlytestterm";
        await using (var db = _fx.NewContext())
        {
            if (!await db.Wordlists.AnyAsync(w => w.MarketCode == "EG" && w.Term == egOnlyTerm))
            {
                db.Wordlists.Add(new ReviewsFilterWordlist
                {
                    MarketCode = "EG",
                    Term = egOnlyTerm,
                    CreatedByActorId = Guid.Empty,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }
        }

        var filter = NewFilter();
        var sa = await filter.EvaluateAsync("SA", $"this contains {egOnlyTerm}");
        var eg = await filter.EvaluateAsync("EG", $"this contains {egOnlyTerm}");
        sa.Tripped.Should().BeFalse($"'{egOnlyTerm}' is seeded only on EG");
        eg.Tripped.Should().BeTrue();
    }

    [Fact]
    public async Task Multiple_matched_terms_are_all_returned_distinct()
    {
        var filter = NewFilter();
        var result = await filter.EvaluateAsync("SA", "FRAUD and SCAM and fake review combined");
        result.Tripped.Should().BeTrue();
        result.MatchedTerms.Should().Contain(new[] { "fraud", "scam", "fake review" });
        result.MatchedTerms.Distinct().Should().HaveSameCount(result.MatchedTerms);
    }

    [Fact]
    public async Task Empty_market_or_text_returns_clean()
    {
        var filter = NewFilter();
        (await filter.EvaluateAsync("", "anything")).Tripped.Should().BeFalse();
        (await filter.EvaluateAsync("SA")).Tripped.Should().BeFalse();
        (await filter.EvaluateAsync("SA", (string?)null!)).Tripped.Should().BeFalse();
    }

    [Fact]
    public async Task Multiple_text_inputs_are_scanned_independently()
    {
        var filter = NewFilter();
        // Headline clean, body trips — same as how SubmitReviewHandler invokes it.
        var result = await filter.EvaluateAsync("SA", "great product", "this is fraud");
        result.Tripped.Should().BeTrue();
        result.MatchedTerms.Should().Contain("fraud");
    }

    private ProfanityFilter NewFilter()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(_fx.ConnectionString));
        return new ProfanityFilter(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new ArabicNormalizer(),
            TimeSpan.Zero);
    }
}
