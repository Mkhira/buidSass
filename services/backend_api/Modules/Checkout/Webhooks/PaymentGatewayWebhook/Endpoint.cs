using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives.Payment;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Checkout.Webhooks.PaymentGatewayWebhook;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapPaymentGatewayWebhookEndpoint(this IEndpointRouteBuilder builder)
    {
        // Providers retry aggressively on non-2xx (R7) — we always return 2xx unless signature
        // verification fails, in which case 401 silent (no detail).
        builder.MapPost("/webhooks/payment-gateway/{providerId}", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        string providerId,
        HttpContext context,
        CheckoutDbContext db,
        IEnumerable<IPaymentGateway> gateways,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Checkout.Webhook");
        var gateway = gateways.FirstOrDefault(g => string.Equals(g.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        if (gateway is null)
        {
            logger.LogWarning("checkout.webhook.unknown_provider providerId={ProviderId}", providerId);
            return Results.StatusCode(404);
        }

        string rawPayload;
        using (var reader = new StreamReader(context.Request.Body))
        {
            rawPayload = await reader.ReadToEndAsync(ct);
        }
        var signature = context.Request.Headers["X-Signature"].ToString();
        var eventType = context.Request.Headers["X-Event-Type"].ToString();
        var providerEventId = context.Request.Headers["X-Event-Id"].ToString();
        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            logger.LogWarning("checkout.webhook.missing_event_id providerId={ProviderId}", providerId);
            return Results.StatusCode(200);
        }

        var envelope = new WebhookEnvelope(providerId, signature, eventType, providerEventId, rawPayload);
        var translation = await gateway.HandleWebhookAsync(envelope, ct);
        if (translation is null)
        {
            // Signature-invalid or unknown — per R7 we still return 2xx so the provider doesn't
            // hammer us with retries. 401 is reserved for actively-hostile clients.
            return Results.StatusCode(200);
        }

        // SC-007 dedup: try insert; unique (provider_id, provider_event_id) makes duplicates 23505.
        var record = new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            ProviderId = providerId,
            ProviderEventId = providerEventId,
            EventType = eventType,
            SignatureVerified = true,
            ReceivedAt = DateTimeOffset.UtcNow,
            RawPayload = rawPayload,
        };
        db.PaymentWebhookEvents.Add(record);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
        {
            logger.LogInformation("checkout.webhook.duplicate providerId={ProviderId} eventId={EventId}", providerId, providerEventId);
            return Results.StatusCode(200);
        }

        // Update the matching attempt.
        var attempt = await db.PaymentAttempts
            .Where(a => a.ProviderId == providerId && a.ProviderTxnId == translation.ProviderTxnId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (attempt is not null)
        {
            attempt.State = translation.MappedAttemptState;
            attempt.ErrorCode = translation.ErrorCode;
            attempt.ErrorMessage = translation.ErrorMessage;
            attempt.UpdatedAt = DateTimeOffset.UtcNow;
            record.HandledAt = attempt.UpdatedAt;
            await db.SaveChangesAsync(ct);
        }
        return Results.StatusCode(200);
    }
}
