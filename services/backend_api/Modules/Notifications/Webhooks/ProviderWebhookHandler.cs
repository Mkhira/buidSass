using BackendApi.Modules.Notifications.Domain;
using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using BackendApi.Modules.Notifications.Providers;
using BackendApi.Modules.Notifications.Workers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Webhooks;

/// <summary>
/// T031 — shared webhook handler invoked by 6 thin per-provider endpoints
/// (ses / sendgrid / unifonic / vodafone-egypt / infobip / fcm). The handler
/// enforces V-3 (signature fail-closed → 401), V-6 (idempotent on composite
/// PK (provider_id, provider_message_id, event_kind)), and AC-26 (signature
/// valid + duplicate re-delivery returns 200).
///
/// Successful first-receipt also advances the matching <see cref="Notification"/>
/// row through the state machine when the canonical event maps to a terminal
/// state (delivered / failed / bounced).
/// </summary>
public sealed class ProviderWebhookHandler
{
    private readonly NotificationProviderRouter _router;
    private readonly TimeProvider _clock;

    public ProviderWebhookHandler(NotificationProviderRouter router, TimeProvider clock)
    {
        _router = router;
        _clock = clock;
    }

    public async Task<IResult> HandleAsync(
        string providerId,
        HttpRequest request,
        NotificationsDbContext db,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var provider = _router.ResolveById(providerId);
        if (provider is null) return Results.NotFound();

        // Buffer the raw body so signature validation sees exactly the bytes
        // the provider signed. ASP.NET Core defaults a non-rewindable body
        // stream; the EnableBuffering call ahead of this method makes it
        // seekable.
        request.EnableBuffering();
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, ct);
        var rawBody = ms.ToArray();
        request.Body.Position = 0;

        // Pull KV secrets visible to the provider's signature validator. In a
        // real deployment these come from Key Vault via IConfiguration; for
        // sandbox they live in appsettings under "notifications:secrets:*".
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in configuration.GetSection("notifications:secrets").AsEnumerable(makePathsRelative: true))
        {
            if (kv.Value is null) continue;
            // Convert appsettings dot-path to slash-path expected by provider lookups.
            secrets[kv.Key.Replace(':', '/')] = kv.Value;
        }

        if (!provider.ValidateWebhookSignature(request, rawBody, secrets))
            return Results.Unauthorized();

        var ev = provider.ParseWebhookEvent(request, rawBody);
        if (string.IsNullOrEmpty(ev.ProviderMessageId))
            return Results.Ok(); // unparseable but signed — drop silently per AC-26 audit-only.

        // V-6 idempotency check on composite PK.
        var alreadySeen = await db.WebhooksReceived
            .AnyAsync(w => w.ProviderId == ev.ProviderId
                && w.ProviderMessageId == ev.ProviderMessageId
                && w.EventKind == ev.EventKind, ct);
        if (alreadySeen) return Results.Ok();

        db.WebhooksReceived.Add(new WebhookReceived
        {
            ProviderId = ev.ProviderId,
            ProviderMessageId = ev.ProviderMessageId,
            EventKind = ev.EventKind,
            ReceivedAt = _clock.GetUtcNow(),
            SignatureValidated = true,
        });

        // Advance the corresponding notification row if we can find it by
        // (provider_id, provider_message_id).
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.ProviderId == ev.ProviderId
                && n.ProviderMessageId == ev.ProviderMessageId
                && n.DeletedAt == null, ct);

        if (notification is not null)
        {
            var nextState = ev.CanonicalEventKind switch
            {
                CanonicalWebhookEventKinds.Delivered =>
                    NotificationsConstants.NotificationStates.Delivered,
                CanonicalWebhookEventKinds.Bounced
                    or CanonicalWebhookEventKinds.Failed
                    or CanonicalWebhookEventKinds.Unregistered
                    or CanonicalWebhookEventKinds.Complaint =>
                    NotificationsConstants.NotificationStates.Failed,
                _ => null,
            };
            if (nextState is not null
                && notification.State != nextState
                && NotificationStateMachine.CanTransition(notification.State, nextState))
            {
                NotificationStateMachine.EnsureTransition(notification.State, nextState);
                notification.State = nextState;
                notification.UpdatedAt = _clock.GetUtcNow();
                if (nextState == NotificationsConstants.NotificationStates.Delivered)
                    notification.DeliveredAt = _clock.GetUtcNow();
                if (nextState == NotificationsConstants.NotificationStates.Failed)
                {
                    notification.FailedAt = _clock.GetUtcNow();
                    notification.FailedReason = ev.ErrorCode;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }
}
