using System.Collections.Concurrent;

namespace BackendApi.Modules.Catalog.Primitives.Restriction;

public sealed class RestrictionCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public bool TryGet(Guid productId, string marketCode, string verificationState, out RestrictionDecision decision)
    {
        var key = Key(productId, marketCode, verificationState);
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            decision = entry.Decision;
            return true;
        }

        decision = default!;
        return false;
    }

    public void Set(Guid productId, string marketCode, string verificationState, RestrictionDecision decision)
    {
        var key = Key(productId, marketCode, verificationState);
        _entries[key] = new CacheEntry(decision, DateTimeOffset.UtcNow.Add(DefaultTtl));
    }

    public void InvalidateProduct(Guid productId)
    {
        var prefix = $"{productId:N}:";
        foreach (var key in _entries.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    public void Clear() => _entries.Clear();

    private static string Key(Guid productId, string marketCode, string verificationState)
    {
        return $"{productId:N}:{marketCode}:{verificationState}";
    }

    private readonly record struct CacheEntry(RestrictionDecision Decision, DateTimeOffset ExpiresAt);
}
