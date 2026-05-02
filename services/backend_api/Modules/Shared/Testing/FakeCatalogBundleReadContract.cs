using System.Collections.Concurrent;

namespace BackendApi.Modules.Shared.Testing;

/// <summary>In-process fake of <see cref="ICatalogBundleReadContract"/>.</summary>
public sealed class FakeCatalogBundleReadContract : ICatalogBundleReadContract
{
    private readonly ConcurrentDictionary<Guid, CatalogBundleRead> _byId = new();

    public FakeCatalogBundleReadContract WithBundle(CatalogBundleRead read)
    {
        _byId[read.BundleId] = read;
        return this;
    }

    public Task<CatalogBundleRead> ReadAsync(Guid bundleId, string marketCode, CancellationToken ct)
    {
        if (_byId.TryGetValue(bundleId, out var hit)) return Task.FromResult(hit);
        return Task.FromResult(new CatalogBundleRead(
            BundleId: bundleId,
            MarketCode: marketCode,
            DisplayNameAr: $"Bundle-{bundleId.ToString()[..8]}-AR",
            DisplayNameEn: $"Bundle-{bundleId.ToString()[..8]}-EN",
            IsAvailable: true,
            UnavailableReason: null));
    }
}
