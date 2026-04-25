using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
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

        // SC-007 dedup + atomic apply: insert the dedup row AND mutate the matching attempt
        // in one transaction. Without this, an insert that succeeded but failed to commit the
        // attempt state (process crash, transient error) leaves a `HandledAt is null` row that
        // the provider's retry would short-circuit at 23505 — losing the state mutation forever.
        // CR review on PR #30: the duplicate path now resumes processing if HandledAt is null.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

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

        var resumeExisting = false;
        db.PaymentWebhookEvents.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
        {
            // Duplicate provider event id. If the prior delivery completed (HandledAt set), skip;
            // otherwise pick up where the previous handler left off so the attempt mutation is
            // not silently lost.
            db.Entry(record).State = EntityState.Detached;
            var existing = await db.PaymentWebhookEvents
                .SingleOrDefaultAsync(e => e.ProviderId == providerId && e.ProviderEventId == providerEventId, ct);
            if (existing is null || existing.HandledAt is not null)
            {
                logger.LogInformation(
                    "checkout.webhook.duplicate providerId={ProviderId} eventId={EventId} alreadyHandled={Handled}",
                    providerId, providerEventId, existing?.HandledAt is not null);
                await tx.RollbackAsync(ct);
                return Results.StatusCode(200);
            }
            record = existing;
            resumeExisting = true;
            logger.LogInformation(
                "checkout.webhook.resume providerId={ProviderId} eventId={EventId}",
                providerId, providerEventId);
        }

        // Update the matching attempt + stamp HandledAt — same transaction as the dedup row.
        var attempt = await db.PaymentAttempts
            .Where(a => a.ProviderId == providerId && a.ProviderTxnId == translation.ProviderTxnId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (attempt is not null)
        {
            // CR review on PR #30 round 2: route through the state-machine helper so a late /
            // out-of-order webhook can't move a terminal attempt backwards (e.g.,
            // captured -> declined). Invalid transitions are logged + ignored, but we still
            // 200 to the provider so they stop retrying.
            var nowUtc = DateTimeOffset.UtcNow;
            if (PaymentAttemptStates.TryTransition(attempt, translation.MappedAttemptState, nowUtc))
            {
                attempt.ErrorCode = translation.ErrorCode;
                attempt.ErrorMessage = translation.ErrorMessage;
                record.HandledAt = nowUtc;
            }
            else
            {
                logger.LogWarning(
                    "checkout.webhook.invalid_transition providerId={ProviderId} eventId={EventId} from={From} to={To}",
                    providerId, providerEventId, attempt.State, translation.MappedAttemptState);
                // Mark the event handled so retries stop; operator surface for invalid edges.
                record.HandledAt = nowUtc;
            }
        }
        else
        {
            // No matching attempt: still mark the event handled so a retry doesn't repeat the
            // lookup forever. Operator reconciliation handles the orphan case.
            record.HandledAt = DateTimeOffset.UtcNow;
            logger.LogWarning(
                "checkout.webhook.no_matching_attempt providerId={ProviderId} txnId={TxnId} resumeExisting={Resume}",
                providerId, translation.ProviderTxnId, resumeExisting);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.StatusCode(200);
    }
}
