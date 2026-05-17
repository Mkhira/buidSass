using BackendApi.Modules.Shared;
using BackendApi.Modules.Shipping.Primitives;
using BackendApi.Modules.Shipping.Providers;
using BackendApi.Modules.Shipping.Providers.Aramex;
using BackendApi.Modules.Shipping.Providers.Bosta;
using BackendApi.Modules.Shipping.Providers.Smsa;
using BackendApi.Modules.Shipping.Services;
using BackendApi.Modules.Shipping.Subscribers;
using BackendApi.Modules.Shipping.Webhooks;
using BackendApi.Modules.Shipping.Workers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BackendApi.Modules.Shipping;

/// <summary>
/// Phase 3 — providers, label dispatch worker, webhook receivers, and the
/// order-lifecycle subscriber. Each provider is registered as a singleton
/// IShippingProvider and aggregated through <see cref="ProviderRegistry"/>.
/// </summary>
public static partial class ShippingModule
{
    static partial void AddWorkers(IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        // Provider HTTP clients — each gets its own typed client so future
        // Polly retry / circuit-breaker policies attach per provider.
        services.AddHttpClient<SmsaProvider>();
        services.AddHttpClient<AramexKsaProvider>();
        services.AddHttpClient<AramexEgProvider>();
        services.AddHttpClient<BostaProvider>();

        services.AddSingleton<IShippingProvider>(sp => sp.GetRequiredService<SmsaProvider>());
        services.AddSingleton<IShippingProvider>(sp => sp.GetRequiredService<AramexKsaProvider>());
        services.AddSingleton<IShippingProvider>(sp => sp.GetRequiredService<AramexEgProvider>());
        services.AddSingleton<IShippingProvider>(sp => sp.GetRequiredService<BostaProvider>());
        services.AddSingleton<ProviderRegistry>();

        services.AddScoped<IProviderSecretResolver, ConfigurationProviderSecretResolver>();
        services.AddScoped<WebhookHandler>();
        services.AddScoped<ShipmentService>();
        services.AddSingleton<ILabelStorage, PlaceholderLabelStorage>();

        services.AddHostedService<LabelDispatchWorker>();
    }

    static partial void AddSubscribers(IServiceCollection services)
    {
        // Per project memory — cross-module hooks live in Modules/Shared/.
        services.AddScoped<OrderLifecycleSubscriber>();
        services.AddScoped<IOrderLifecycleSubscriber>(sp =>
            sp.GetRequiredService<OrderLifecycleSubscriber>());
    }

    static partial void MapWebhookEndpoints(IEndpointRouteBuilder webhooks)
    {
        // §3 — four webhook endpoints, one per provider. Each one delegates
        // to the same WebhookHandler so the auth + idempotency + state
        // logic stays in one place.
        webhooks.MapPost("/smsa", (HttpRequest req, WebhookHandler h, CancellationToken ct) =>
            h.HandleAsync(ShippingConstants.Providers.Smsa, req, ct))
            .AllowAnonymous()
            .WithName("shipping_webhook_smsa")
            .WithTags("shipping-webhooks");

        webhooks.MapPost("/aramex-ksa", (HttpRequest req, WebhookHandler h, CancellationToken ct) =>
            h.HandleAsync(ShippingConstants.Providers.AramexKsa, req, ct))
            .AllowAnonymous()
            .WithName("shipping_webhook_aramex_ksa")
            .WithTags("shipping-webhooks");

        webhooks.MapPost("/aramex-eg", (HttpRequest req, WebhookHandler h, CancellationToken ct) =>
            h.HandleAsync(ShippingConstants.Providers.AramexEg, req, ct))
            .AllowAnonymous()
            .WithName("shipping_webhook_aramex_eg")
            .WithTags("shipping-webhooks");

        webhooks.MapPost("/bosta", (HttpRequest req, WebhookHandler h, CancellationToken ct) =>
            h.HandleAsync(ShippingConstants.Providers.Bosta, req, ct))
            .AllowAnonymous()
            .WithName("shipping_webhook_bosta")
            .WithTags("shipping-webhooks");
    }
}
