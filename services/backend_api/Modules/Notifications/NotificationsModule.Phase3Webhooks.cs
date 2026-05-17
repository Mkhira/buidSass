using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using BackendApi.Modules.Notifications.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace BackendApi.Modules.Notifications;

/// <summary>
/// T031 — fills the <see cref="MapWebhookEndpoints"/> partial. Six routes,
/// one per provider, all delegating to <see cref="ProviderWebhookHandler"/>
/// so signature-fail-closed / idempotency / state-advance live in one place.
/// </summary>
public static partial class NotificationsModule
{
    static partial void MapWebhookEndpoints(IEndpointRouteBuilder webhooks)
    {
        MapOne(webhooks, "ses", NotificationsConstants.Providers.Ses);
        MapOne(webhooks, "sendgrid", NotificationsConstants.Providers.SendGrid);
        MapOne(webhooks, "unifonic", NotificationsConstants.Providers.Unifonic);
        MapOne(webhooks, "vodafone-egypt", NotificationsConstants.Providers.VodafoneEgypt);
        MapOne(webhooks, "infobip", NotificationsConstants.Providers.Infobip);
        MapOne(webhooks, "fcm", NotificationsConstants.Providers.Fcm);
    }

    private static void MapOne(IEndpointRouteBuilder webhooks, string routeSegment, string providerId)
    {
        webhooks.MapPost($"/{routeSegment}", async (
            HttpRequest request,
            NotificationsDbContext db,
            IConfiguration configuration,
            ProviderWebhookHandler handler,
            CancellationToken ct) =>
        {
            return await handler.HandleAsync(providerId, request, db, configuration, ct);
        }).WithName($"notifications.webhook.{routeSegment}");
    }
}
