namespace BackendApi.Modules.Payments.Providers;

/// <summary>
/// Resolves <see cref="IPaymentProvider"/> instances by provider id. The
/// registry is populated via DI iteration over every registered
/// <see cref="IPaymentProvider"/> singleton (see
/// <c>PaymentsModule.Providers.cs</c>).
/// </summary>
public sealed class ProviderRegistry
{
    private readonly Dictionary<string, IPaymentProvider> _byId;

    public ProviderRegistry(IEnumerable<IPaymentProvider> providers)
    {
        _byId = providers.ToDictionary(p => p.ProviderId, StringComparer.Ordinal);
    }

    public IEnumerable<IPaymentProvider> All => _byId.Values;

    public bool TryResolve(string providerId, out IPaymentProvider? provider)
    {
        var found = _byId.TryGetValue(providerId, out var p);
        provider = p;
        return found;
    }

    /// <summary>Returns the provider instance, or throws if not registered.</summary>
    public IPaymentProvider Get(string providerId) =>
        _byId.TryGetValue(providerId, out var p)
            ? p
            : throw new InvalidOperationException($"Provider '{providerId}' is not registered.");
}
