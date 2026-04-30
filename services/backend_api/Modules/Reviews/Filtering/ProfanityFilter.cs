using System.Collections.Concurrent;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Search.Primitives.Normalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Reviews.Filtering;

/// <summary>
/// In-process per-market profanity wordlist evaluator. Caches the active set
/// per market and refreshes every 60 s + on explicit invalidation triggered
/// by wordlist mutations (research §R13). Term comparisons use the Arabic
/// normalizer from <see cref="IArabicNormalizer"/> so AR diacritics, hamza
/// variants, and elongations match the same way they were stored.
/// </summary>
public sealed class ProfanityFilter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IArabicNormalizer _normalizer;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, MarketCache> _byMarket = new(StringComparer.OrdinalIgnoreCase);

    public ProfanityFilter(IServiceScopeFactory scopeFactory, IArabicNormalizer normalizer)
        : this(scopeFactory, normalizer, TimeSpan.FromSeconds(60))
    {
    }

    /// <summary>Test-only constructor that exposes the TTL.</summary>
    public ProfanityFilter(IServiceScopeFactory scopeFactory, IArabicNormalizer normalizer, TimeSpan cacheTtl)
    {
        _scopeFactory = scopeFactory;
        _normalizer = normalizer;
        _cacheTtl = cacheTtl;
    }

    public ProfanityFilterResult Evaluate(string marketCode, params string[] textsToScan)
    {
        if (string.IsNullOrEmpty(marketCode) || textsToScan is null || textsToScan.Length == 0)
        {
            return ProfanityFilterResult.Clean;
        }

        var terms = GetOrLoad(marketCode);
        if (terms.Count == 0)
        {
            return ProfanityFilterResult.Clean;
        }

        var matched = new List<string>();
        foreach (var raw in textsToScan)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var normalized = _normalizer.Normalize(raw);
            if (string.IsNullOrEmpty(normalized)) continue;

            // Tokenize on whitespace and check each token + the full string for
            // multi-word terms. The wordlist is small (~hundreds of entries),
            // so an O(N*M) scan per submission is well under budget.
            foreach (var term in terms)
            {
                if (normalized.Contains(term, StringComparison.Ordinal))
                {
                    matched.Add(term);
                }
            }
        }

        if (matched.Count == 0) return ProfanityFilterResult.Clean;
        return new ProfanityFilterResult(true, matched.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Drops cached terms for the given market so the next call reloads from DB.</summary>
    public void Invalidate(string marketCode) => _byMarket.TryRemove(marketCode, out _);

    private IReadOnlyCollection<string> GetOrLoad(string marketCode)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        if (_byMarket.TryGetValue(marketCode, out var cached) && nowUtc - cached.LoadedAtUtc < _cacheTtl)
        {
            return cached.Terms;
        }

        // Reload synchronously — first request after TTL expiry pays a single
        // round-trip; subsequent requests within the window are in-process.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        var terms = db.Wordlists
            .AsNoTracking()
            .Where(w => w.MarketCode == marketCode)
            .Select(w => w.Term)
            .ToList();

        var fresh = new MarketCache(nowUtc, terms);
        _byMarket[marketCode] = fresh;
        return fresh.Terms;
    }

    private sealed record MarketCache(DateTimeOffset LoadedAtUtc, IReadOnlyCollection<string> Terms);
}

/// <summary>Outcome of a single profanity scan.</summary>
public sealed record ProfanityFilterResult(bool Tripped, IReadOnlyList<string> MatchedTerms)
{
    public static readonly ProfanityFilterResult Clean = new(false, Array.Empty<string>());
}
