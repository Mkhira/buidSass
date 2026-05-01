using System.Collections.Concurrent;

namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// In-process fake of <see cref="ICatalogProductReadContract"/> for tests.
/// Defaults to "available" for any unknown product id; callers can stub
/// per-id results via <see cref="WithProduct"/>.
/// </summary>
public sealed class FakeCatalogProductReadContract : ICatalogProductReadContract
{
    private readonly ConcurrentDictionary<Guid, CatalogProductRead> _byId = new();

    public FakeCatalogProductReadContract WithProduct(CatalogProductRead read)
    {
        _byId[read.ProductId] = read;
        return this;
    }

    public Task<CatalogProductRead> ReadAsync(Guid productId, string marketCode, CancellationToken ct)
    {
        if (_byId.TryGetValue(productId, out var hit)) return Task.FromResult(hit);
        return Task.FromResult(new CatalogProductRead(
            ProductId: productId,
            MarketCode: marketCode,
            DisplayNameAr: $"Product-{productId.ToString()[..8]}-AR",
            DisplayNameEn: $"Product-{productId.ToString()[..8]}-EN",
            VendorId: null,
            IsAvailable: true,
            UnavailableReason: null));
    }
}
