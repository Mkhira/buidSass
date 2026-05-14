namespace BackendApi.Modules.Shipping.Providers;

/// <summary>
/// Resolves an <see cref="IShippingProvider"/> by <c>provider_id</c>. All
/// provider implementations register against the same key string declared
/// in <see cref="Primitives.ShippingConstants.Providers"/>, so business
/// logic never branches on provider type (Principle 14).
/// </summary>
public sealed class ProviderRegistry
{
    private readonly Dictionary<string, IShippingProvider> _byId;

    public ProviderRegistry(IEnumerable<IShippingProvider> providers)
    {
        _byId = providers.ToDictionary(p => p.ProviderId, StringComparer.Ordinal);
    }

    public IShippingProvider Resolve(string providerId)
    {
        if (!_byId.TryGetValue(providerId, out var provider))
        {
            throw new InvalidOperationException(
                $"No provider registered for id '{providerId}'.");
        }
        return provider;
    }

    public bool TryResolve(string providerId, out IShippingProvider? provider)
    {
        if (_byId.TryGetValue(providerId, out var found))
        {
            provider = found;
            return true;
        }
        provider = null;
        return false;
    }

    public IEnumerable<IShippingProvider> All => _byId.Values;
}
