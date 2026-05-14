using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Providers;
using BackendApi.Modules.Shipping.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Shipping.Webhooks;

/// <summary>
/// Single-entry handler for inbound provider webhooks. Validates the HMAC
/// signature (V-3 fail-closed), de-duplicates on the composite PK (BR-6 /
/// AC-11), parses the canonical event, and delegates state advancement to
/// <see cref="ShipmentService.ApplyWebhookAsync"/>.
/// </summary>
public sealed class WebhookHandler(
    ShippingDbContext db,
    ProviderRegistry providers,
    IProviderSecretResolver secrets,
    ShipmentService shipments,
    ILogger<WebhookHandler> logger)
{
    public async Task<IResult> HandleAsync(string providerId, HttpRequest request, CancellationToken ct)
    {
        if (!providers.TryResolve(providerId, out var provider) || provider is null)
        {
            return Results.NotFound(new { error = "unknown_provider" });
        }

        // Read the raw body once — required for HMAC validation and for the
        // canonical parser.
        request.EnableBuffering();
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, ct);
        var rawBody = ms.ToArray();
        request.Body.Position = 0;

        // V-3 — fail-closed on bad signature.
        var providerSecrets = secrets.ResolveSecrets(providerId);
        if (!provider.ValidateWebhookSignature(request, rawBody, providerSecrets))
        {
            logger.LogWarning("Webhook signature invalid for provider {Provider}", providerId);
            return Results.Unauthorized();
        }

        WebhookEvent canonical;
        try
        {
            canonical = provider.ParseWebhookEvent(request, rawBody);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException || ex is InvalidOperationException)
        {
            logger.LogWarning(ex, "Webhook body malformed for provider {Provider}", providerId);
            return Results.BadRequest(new { error = "malformed_payload" });
        }

        if (string.IsNullOrWhiteSpace(canonical.ProviderTrackingId))
        {
            return Results.BadRequest(new { error = "missing_tracking_id" });
        }

        // BR-6 — idempotency check via composite PK. Duplicates short-circuit
        // with 200 OK so the provider does not see retries as failures.
        var alreadyAccepted = await db.WebhooksReceived
            .AnyAsync(w =>
                w.ProviderId == providerId &&
                w.ProviderTrackingId == canonical.ProviderTrackingId &&
                w.EventKind == canonical.ProviderEventKind &&
                w.OccurredAt == canonical.OccurredAt, ct);
        if (alreadyAccepted)
        {
            return Results.Ok(new { idempotent = true });
        }

        // Locate the shipment via (provider_id, provider_tracking_id).
        var shipment = await db.Shipments.FirstOrDefaultAsync(s =>
            s.ProviderId == providerId &&
            s.ProviderTrackingId == canonical.ProviderTrackingId &&
            s.DeletedAt == null, ct);

        // Record the webhook idempotency row (so retries don't double-process)
        // BEFORE acting on the shipment. If the shipment is unknown we still
        // mark the webhook as received so we don't spin loop retrying.
        db.WebhooksReceived.Add(new WebhookReceived
        {
            ProviderId = providerId,
            ProviderTrackingId = canonical.ProviderTrackingId,
            EventKind = canonical.ProviderEventKind,
            OccurredAt = canonical.OccurredAt,
            SignatureValidated = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        if (shipment is null)
        {
            // Orphan event — log + persist for the daily reconciliation job (Edge Case).
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Orphan webhook: provider={Provider} tracking_id={Tracking} kind={Kind}",
                providerId, canonical.ProviderTrackingId, canonical.ProviderEventKind);
            return Results.Ok(new { orphan = true });
        }

        await db.SaveChangesAsync(ct);

        // Apply the canonical event — BR-12 precedence resolution is inside.
        await shipments.ApplyWebhookAsync(shipment, canonical, ct);
        return Results.Ok(new { processed = true });
    }
}
