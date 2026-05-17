using System.Security.Cryptography;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using BackendApi.Modules.Payments.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Payments.Webhooks;

/// <summary>
/// T045 — single-entry webhook handler. Per-provider endpoints map to this
/// class. Behavior:
///  1. Read raw body (HMAC requires the exact bytes).
///  2. Validate the provider's signature; V-3 fail-closed (401) on mismatch.
///  3. Parse the canonical event.
///  4. Idempotency check via the (provider, message_id, kind) composite PK.
///  5. Locate the matching Payment by (provider, message_id) and apply the
///     canonical event onto its state via <see cref="PaymentTransitionService"/>.
/// </summary>
public sealed class PaymentsWebhookHandler
{
    private readonly PaymentsDbContext _db;
    private readonly ProviderRegistry _providers;
    private readonly IProviderSecretResolver _secrets;
    private readonly PaymentTransitionService _transitions;
    private readonly IAuditEventPublisher _audit;
    private readonly ILogger<PaymentsWebhookHandler> _logger;
    private readonly TimeProvider _clock;

    public PaymentsWebhookHandler(
        PaymentsDbContext db,
        ProviderRegistry providers,
        IProviderSecretResolver secrets,
        PaymentTransitionService transitions,
        IAuditEventPublisher audit,
        ILogger<PaymentsWebhookHandler> logger,
        TimeProvider clock)
    {
        _db = db;
        _providers = providers;
        _secrets = secrets;
        _transitions = transitions;
        _audit = audit;
        _logger = logger;
        _clock = clock;
    }

    public async Task<IResult> HandleAsync(string providerId, HttpRequest request, CancellationToken ct)
    {
        if (!_providers.TryResolve(providerId, out var provider) || provider is null)
        {
            return Results.NotFound(new { error = "unknown_provider" });
        }

        request.EnableBuffering();
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, ct);
        var rawBody = ms.ToArray();
        request.Body.Position = 0;

        // V-3 fail-closed.
        var providerSecrets = _secrets.ResolveSecrets(providerId);
        if (!provider.ValidateWebhookSignature(request, rawBody, providerSecrets))
        {
            _logger.LogWarning("Webhook signature invalid for provider {Provider}", providerId);
            return Results.Unauthorized();
        }

        WebhookEvent canonical;
        try
        {
            canonical = provider.ParseWebhookEvent(request, rawBody);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Webhook body malformed for provider {Provider}", providerId);
            return Results.BadRequest(new { error = "malformed_payload" });
        }

        if (string.IsNullOrWhiteSpace(canonical.ProviderMessageId))
        {
            return Results.BadRequest(new { error = "missing_provider_message_id" });
        }

        // BR-4 — idempotency. Returns 200 OK on duplicate so providers stop retrying.
        var alreadyReceived = await _db.WebhooksReceived.AnyAsync(w =>
            w.ProviderId == providerId
            && w.ProviderMessageId == canonical.ProviderMessageId
            && w.EventKind == canonical.EventKind, ct);
        if (alreadyReceived)
        {
            return Results.Ok(new { idempotent = true });
        }

        _db.WebhooksReceived.Add(new WebhookReceived
        {
            ProviderId = providerId,
            ProviderMessageId = canonical.ProviderMessageId,
            EventKind = canonical.EventKind,
            ReceivedAt = _clock.GetUtcNow(),
            SignatureValidated = true,
            BodyHash = Convert.ToHexString(SHA256.HashData(rawBody)).ToLowerInvariant(),
        });

        var payment = await _db.Payments.FirstOrDefaultAsync(p =>
            p.ProviderId == providerId
            && p.ProviderMessageId == canonical.ProviderMessageId
            && p.DeletedAt == null, ct);
        if (payment is null)
        {
            // Orphan event — log + persist for daily reconciliation to surface as orphan_provider_row.
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Orphan webhook: provider={Provider} message_id={MsgId} kind={Kind}",
                providerId, canonical.ProviderMessageId, canonical.EventKind);
            return Results.Ok(new { orphan = true });
        }

        await _db.SaveChangesAsync(ct);

        try
        {
            switch (canonical.CanonicalEventKind)
            {
                case CanonicalWebhookEventKinds.Captured:
                    if (payment.State == PaymentsConstants.PaymentStates.Captured) break;
                    await _transitions.CaptureAsync(_db, payment, ct);
                    break;
                case CanonicalWebhookEventKinds.Failed:
                    if (PaymentsConstants.PaymentStates.Terminal.Contains(payment.State)) break;
                    var failedReason = canonical.EventKind.Contains("declined", StringComparison.OrdinalIgnoreCase)
                        ? PaymentsConstants.FailedReasons.Declined
                        : PaymentsConstants.FailedReasons.ProviderUnavailable;
                    await _transitions.MarkFailedAsync(_db, payment, failedReason, ct);
                    break;
                case CanonicalWebhookEventKinds.Expired:
                    if (PaymentsConstants.PaymentStates.Terminal.Contains(payment.State)) break;
                    await _transitions.MarkExpiredAsync(_db, payment, PaymentsConstants.ExpiredReasons.RedirectTimeout, ct);
                    break;
                case CanonicalWebhookEventKinds.Chargeback:
                    _db.Chargebacks.Add(new Chargeback
                    {
                        Id = Guid.NewGuid(),
                        PaymentId = payment.Id,
                        ProviderChargebackId = canonical.ProviderMessageId,
                        Amount = canonical.Amount ?? payment.Amount,
                        ReasonCode = canonical.EventKind,
                        ReceivedAt = canonical.OccurredAt,
                        Status = PaymentsConstants.ChargebackStatuses.Received,
                        CreatedAt = _clock.GetUtcNow(),
                        UpdatedAt = _clock.GetUtcNow(),
                    });
                    await _db.SaveChangesAsync(ct);
                    if (payment.State != PaymentsConstants.PaymentStates.ChargebackReceived)
                    {
                        await _transitions.MarkChargebackAsync(_db, payment, ct);
                    }
                    break;
                case CanonicalWebhookEventKinds.Refunded:
                    // Refund webhooks confirm a refund the operator already initiated via API.
                    // No state machine push here — the refund flow's completion path owns it.
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Webhook state transition rejected for payment {PaymentId}", payment.Id);
            return Results.Ok(new { ignored = true, reason = "invalid_transition" });
        }

        return Results.Ok(new { processed = true });
    }
}
