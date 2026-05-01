using System.Collections.Concurrent;

namespace BackendApi.Modules.Shared.Testing;

/// <summary>In-process fake of <see cref="ICatalogCategoryReadContract"/>.</summary>
public sealed class FakeCatalogCategoryReadContract : ICatalogCategoryReadContract
{
    private readonly ConcurrentDictionary<Guid, CatalogCategoryRead> _byId = new();

    public FakeCatalogCategoryReadContract WithCategory(CatalogCategoryRead read)
    {
        _byId[read.CategoryId] = read;
        return this;
    }

    public Task<CatalogCategoryRead> ReadAsync(Guid categoryId, string marketCode, CancellationToken ct)
    {
        if (_byId.TryGetValue(categoryId, out var hit)) return Task.FromResult(hit);
        return Task.FromResult(new CatalogCategoryRead(
            CategoryId: categoryId,
            MarketCode: marketCode,
            DisplayNameAr: $"Category-{categoryId.ToString()[..8]}-AR",
            DisplayNameEn: $"Category-{categoryId.ToString()[..8]}-EN",
            IsAvailable: true,
            UnavailableReason: null));
    }
}
