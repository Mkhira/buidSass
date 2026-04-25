using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Checkout.Entities;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Checkout.Primitives;

/// <summary>
/// Centralised audit emission for spec 010 (FR-015). Every CheckoutSession + PaymentAttempt
/// state transition routes through this helper so the action vocabulary stays consistent
/// across slices and the failure mode is uniform — emission is best-effort: a publisher
/// throw must NOT abort the request the customer just succeeded in (logged + swallowed).
///
/// Action vocabulary mirrors `specs/phase-1B/010-checkout/data-model.md` § "Audit + events":
///   `checkout.session.created|addressed|shipping_selected|payment_selected|submitted
///                       |confirmed|failed|expired|admin_expired`
///   `checkout.payment.authorized|captured|declined|voided|refunded|pending_webhook`
///   `checkout.webhook.received|deduped`
/// </summary>
public sealed class CheckoutAuditEmitter(
    IAuditEventPublisher publisher,
    ILogger<CheckoutAuditEmitter> logger)
{
    private const string ActorSystem = "system";
    private const string ActorCustomer = "customer";
    private const string ActorAdmin = "admin";

    public Task EmitSessionTransitionAsync(
        CheckoutSession session,
        string action,
        Guid? actorAccountId,
        string actorRole,
        string? reason,
        CancellationToken ct)
    {
        var actor = actorAccountId ?? CheckoutSystemActors.AuditFallback;
        return SafeEmitAsync(new AuditEvent(
            ActorId: actor,
            ActorRole: actorRole,
            Action: action,
            EntityType: nameof(CheckoutSession),
            EntityId: session.Id,
            BeforeState: null,
            AfterState: new
            {
                session.Id,
                session.AccountId,
                session.CartId,
                session.MarketCode,
                session.State,
                session.PaymentMethod,
                session.ShippingProviderId,
                session.ShippingMethodCode,
                session.OrderId,
                session.FailureReasonCode,
            },
            Reason: reason), ct);
    }

    public Task EmitPaymentTransitionAsync(
        PaymentAttempt attempt,
        string action,
        Guid? actorAccountId,
        string actorRole,
        string? reason,
        CancellationToken ct)
    {
        var actor = actorAccountId ?? CheckoutSystemActors.AuditFallback;
        return SafeEmitAsync(new AuditEvent(
            ActorId: actor,
            ActorRole: actorRole,
            Action: action,
            EntityType: nameof(PaymentAttempt),
            EntityId: attempt.Id,
            BeforeState: null,
            AfterState: new
            {
                attempt.Id,
                attempt.SessionId,
                attempt.ProviderId,
                attempt.Method,
                attempt.AmountMinor,
                attempt.Currency,
                attempt.State,
                attempt.ProviderTxnId,
                attempt.ErrorCode,
            },
            Reason: reason), ct);
    }

    public Task EmitWebhookAsync(
        PaymentWebhookEvent record,
        string action,
        CancellationToken ct)
    {
        return SafeEmitAsync(new AuditEvent(
            // Use the dedicated webhook actor id so audit pivots can isolate webhook-driven
            // mutations from worker / customer / admin activity (CR review PR #31 round 2).
            ActorId: CheckoutSystemActors.Webhook,
            ActorRole: ActorSystem,
            Action: action,
            EntityType: nameof(PaymentWebhookEvent),
            EntityId: record.Id,
            BeforeState: null,
            AfterState: new
            {
                record.Id,
                record.ProviderId,
                record.ProviderEventId,
                record.EventType,
                record.SignatureVerified,
                record.HandledAt,
            },
            Reason: action), ct);
    }

    public static string CustomerRole => ActorCustomer;
    public static string AdminRole => ActorAdmin;
    public static string SystemRole => ActorSystem;

    private async Task SafeEmitAsync(AuditEvent evt, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(evt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Audit failure must NOT abort the customer-facing request — log loud and move on.
            logger.LogWarning(ex,
                "checkout.audit.publish_failed action={Action} entityType={EntityType} entityId={EntityId}",
                evt.Action, evt.EntityType, evt.EntityId);
        }
    }
}

/// <summary>
/// Stable system actor ids for audit rows whose actor is "the platform" (worker tick,
/// webhook ingestion). Distinct from per-module worker actors so audit consumers can pivot.
/// </summary>
public static class CheckoutSystemActors
{
    public static readonly Guid AuditFallback = Guid.Parse("00000000-0000-0000-0000-000000000010");
    public static readonly Guid ExpiryWorker = Guid.Parse("00000000-0000-0000-0000-000000000011");
    public static readonly Guid Webhook = Guid.Parse("00000000-0000-0000-0000-000000000012");
}

/// <summary>Action codes for spec-010 audit events. Centralised so a typo doesn't fork the vocabulary.</summary>
public static class CheckoutAuditActions
{
    public const string SessionCreated = "checkout.session.created";
    public const string SessionAddressed = "checkout.session.addressed";
    public const string SessionShippingSelected = "checkout.session.shipping_selected";
    public const string SessionPaymentSelected = "checkout.session.payment_selected";
    public const string SessionSubmitted = "checkout.session.submitted";
    public const string SessionConfirmed = "checkout.session.confirmed";
    public const string SessionFailed = "checkout.session.failed";
    public const string SessionExpired = "checkout.session.expired";
    public const string SessionAdminExpired = "checkout.session.admin_expired";

    public const string PaymentAuthorized = "checkout.payment.authorized";
    public const string PaymentCaptured = "checkout.payment.captured";
    public const string PaymentDeclined = "checkout.payment.declined";
    public const string PaymentVoided = "checkout.payment.voided";
    public const string PaymentRefunded = "checkout.payment.refunded";
    public const string PaymentFailed = "checkout.payment.failed";
    public const string PaymentPendingWebhook = "checkout.payment.pending_webhook";

    public const string WebhookReceived = "checkout.webhook.received";
    public const string WebhookDeduped = "checkout.webhook.deduped";

    /// <summary>
    /// Map a PaymentAttemptStates value to the matching audit action. CR review on PR #31:
    /// every supported state is enumerated explicitly; an unknown state throws so a typo
    /// can't silently mint a brand-new audit action and fork the vocabulary.
    /// </summary>
    public static string ForAttemptState(string attemptState) => attemptState switch
    {
        PaymentAttemptStates.Authorized => PaymentAuthorized,
        PaymentAttemptStates.Captured => PaymentCaptured,
        PaymentAttemptStates.Declined => PaymentDeclined,
        PaymentAttemptStates.Voided => PaymentVoided,
        PaymentAttemptStates.Refunded => PaymentRefunded,
        PaymentAttemptStates.Failed => PaymentFailed,
        PaymentAttemptStates.PendingWebhook => PaymentPendingWebhook,
        // Initiated is the placeholder before the first real transition — we never audit it
        // standalone, but reject loudly if a caller asks for it instead of silently forging.
        PaymentAttemptStates.Initiated => throw new InvalidOperationException(
            "PaymentAttemptStates.Initiated is the placeholder state and has no audit action."),
        _ => throw new InvalidOperationException(
            $"Unknown payment attempt state '{attemptState}' — refusing to mint a new audit action."),
    };
}
