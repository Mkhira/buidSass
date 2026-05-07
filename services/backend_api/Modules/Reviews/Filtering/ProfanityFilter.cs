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
    // Per-market SemaphoreSlim to coalesce concurrent cache-miss reloads
    // (thundering-herd guard after Invalidate or TTL expiry — M4).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _loadLocks = new(StringComparer.OrdinalIgnoreCase);

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

    public async Task<ProfanityFilterResult> EvaluateAsync(
        string marketCode,
        IReadOnlyList<string?>? textsToScan,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(marketCode) || textsToScan is null || textsToScan.Count == 0)
        {
            return ProfanityFilterResult.Clean;
        }

        var terms = await GetOrLoadAsync(marketCode, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Convenience overload preserving the params-array call shape used by
    /// existing tests. Forwards to <see cref="EvaluateAsync(string, IReadOnlyList{string?}?, CancellationToken)"/>.
    /// </summary>
    public Task<ProfanityFilterResult> EvaluateAsync(string marketCode, params string?[] textsToScan)
        => EvaluateAsync(marketCode, textsToScan, CancellationToken.None);

    /// <summary>Drops cached terms for the given market so the next call reloads from DB.</summary>
    public void Invalidate(string marketCode) => _byMarket.TryRemove(marketCode, out _);

    private async Task<IReadOnlyCollection<string>> GetOrLoadAsync(string marketCode, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        if (_byMarket.TryGetValue(marketCode, out var cached) && nowUtc - cached.LoadedAtUtc < _cacheTtl)
        {
            return cached.Terms;
        }

        // Coalesce concurrent reloaders for this market behind a per-market
        // semaphore. Without this, every concurrent submission after Invalidate()
        // would spin its own DbContext + round-trip — the thundering-herd
        // pattern. With it, only one loader does the DB work; the others
        // re-check the cache after acquiring and reuse the fresh entry.
        var gate = _loadLocks.GetOrAdd(marketCode, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check: another waiter may have populated the cache while
            // we were queued.
            nowUtc = DateTimeOffset.UtcNow;
            if (_byMarket.TryGetValue(marketCode, out var fresh2) && nowUtc - fresh2.LoadedAtUtc < _cacheTtl)
            {
                return fresh2.Terms;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
            var rawTerms = await db.Wordlists
                .AsNoTracking()
                .Where(w => w.MarketCode == marketCode)
                .Select(w => w.Term)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            // Normalize each loaded term against the same canonical form that
            // Evaluate() compares input against (Arabic-normalized + lowercased).
            // UpsertWordlistTermHandler does this at write time, but legacy /
            // hand-edited / migrated rows may not be in canonical form — defending
            // here makes the filter robust to wordlist provenance.
            var normalized = rawTerms
                .Select(t => _normalizer.Normalize(t).ToLowerInvariant())
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var entry = new MarketCache(nowUtc, normalized);
            _byMarket[marketCode] = entry;
            return entry.Terms;
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed record MarketCache(DateTimeOffset LoadedAtUtc, IReadOnlyCollection<string> Terms);
}

/// <summary>Outcome of a single profanity scan.</summary>
public sealed record ProfanityFilterResult(bool Tripped, IReadOnlyList<string> MatchedTerms)
{
    public static readonly ProfanityFilterResult Clean = new(false, Array.Empty<string>());
}
