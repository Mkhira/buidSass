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
        _byId = new Dictionary<string, IShippingProvider>(StringComparer.Ordinal);
        foreach (var p in providers)
        {
            if (_byId.TryGetValue(p.ProviderId, out var conflict))
            {
                // Defensive — DI registration order should make collisions impossible,
                // but a typo in two impls' ProviderId would silently overwrite one
                // under a plain ToDictionary. Surface it at startup with the
                // colliding types so the operator can resolve.
                throw new InvalidOperationException(
                    $"Duplicate IShippingProvider id '{p.ProviderId}' between "
                    + $"{conflict.GetType().FullName} and {p.GetType().FullName}.");
            }
            _byId[p.ProviderId] = p;
        }
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
