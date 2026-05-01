namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// In-process fake of <see cref="IProductDisplayLookup"/>. Returns the
/// supplied dictionary; missing ids return <see langword="null"/> on the
/// single read and are absent from the bulk read.
/// </summary>
public sealed class FakeProductDisplayLookup : IProductDisplayLookup
{
    private readonly IReadOnlyDictionary<Guid, ProductDisplay> _byId;

    public FakeProductDisplayLookup(IReadOnlyDictionary<Guid, ProductDisplay> byId)
    {
        _byId = byId;
    }

    /// <summary>Empty fake — every lookup returns null. Convenient default for tests that don't render products.</summary>
    public static FakeProductDisplayLookup Empty { get; } = new(new Dictionary<Guid, ProductDisplay>());

    public Task<ProductDisplay?> GetAsync(Guid productId, string marketCode, string locale, CancellationToken ct) =>
        Task.FromResult(_byId.TryGetValue(productId, out var display) ? display : null);

    public Task<IReadOnlyDictionary<Guid, ProductDisplay>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        string marketCode,
        string locale,
        CancellationToken ct)
    {
        var subset = productIds
            .Where(_byId.ContainsKey)
            .ToDictionary(id => id, id => _byId[id]);
        return Task.FromResult<IReadOnlyDictionary<Guid, ProductDisplay>>(subset);
    }
}
