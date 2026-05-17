using BackendApi.Modules.Notifications.Providers;

namespace BackendApi.Modules.Notifications.Workers;

/// <summary>
/// Picks the active provider for a (channel, market) pair from the registered
/// <see cref="INotificationProvider"/> collection. The first registration that
/// matches channel and supports the market wins — the registration order in
/// <c>NotificationsModule.Phase3</c> places primary providers ahead of backups.
///
/// Real provider-routing rows (T046) override this default by storing
/// (channel, market) → preferred_provider_id mappings.
/// </summary>
public sealed class NotificationProviderRouter
{
    private readonly IReadOnlyList<INotificationProvider> _providers;

    public NotificationProviderRouter(IEnumerable<INotificationProvider> providers)
    {
        _providers = providers.ToList();
    }

    public INotificationProvider? Resolve(string channel, string marketCode)
    {
        foreach (var p in _providers)
        {
            if (p.Channel == channel && p.SupportsMarket(marketCode)) return p;
        }
        return null;
    }

    public INotificationProvider? ResolveById(string providerId)
    {
        foreach (var p in _providers)
        {
            if (p.ProviderId == providerId) return p;
        }
        return null;
    }
}
