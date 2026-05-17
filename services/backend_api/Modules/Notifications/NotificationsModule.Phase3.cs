using BackendApi.Modules.Notifications.Providers;
using BackendApi.Modules.Notifications.Providers.Fcm;
using BackendApi.Modules.Notifications.Providers.Infobip;
using BackendApi.Modules.Notifications.Providers.SendGrid;
using BackendApi.Modules.Notifications.Providers.Ses;
using BackendApi.Modules.Notifications.Providers.Unifonic;
using BackendApi.Modules.Notifications.Providers.VodafoneEgypt;
using BackendApi.Modules.Notifications.Subscribers;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Notifications;

/// <summary>
/// Phase 3 — provider registrations. Six concrete <see cref="INotificationProvider"/>
/// implementations are registered as singletons; lookup happens through the
/// (DispatchWorker → ProviderRouting) flow added later in this phase, not via
/// constructor injection of a single provider. Per Principle 13/14 substitution
/// guarantee, business logic must not branch on provider id outside the
/// <c>Modules/Notifications/Providers/</c> folder.
/// </summary>
public static partial class NotificationsModule
{
    static partial void AddProviders(IServiceCollection services)
    {
        services.AddSingleton<INotificationProvider, SesEmailProvider>();
        services.AddSingleton<INotificationProvider, SendGridEmailProvider>();
        services.AddSingleton<INotificationProvider, UnifonicSmsProvider>();
        services.AddSingleton<INotificationProvider, VodafoneEgyptSmsProvider>();
        services.AddSingleton<INotificationProvider, InfobipSmsProvider>();
        services.AddSingleton<INotificationProvider, FcmPushProvider>();
    }

    static partial void AddSubscribers(IServiceCollection services)
    {
        // The enqueuer needs DbContext so it's scoped. Subscribers are picked
        // up by MediatR's assembly scan (NotificationsModuleAnchor) — no need
        // to register them here individually.
        services.AddScoped<INotificationEnqueuer, NotificationEnqueuer>();
    }
}
