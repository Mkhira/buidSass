using BackendApi.Modules.Checkout.Entities;

namespace BackendApi.Modules.Checkout.Primitives;

/// <summary>
/// Checkout session state machine (Principle 24 — explicit state machine). The column is
/// citext; a DB CHECK constraint enforces the enum. This class centralises allowed values +
/// legal transitions for the application, matching the table in spec 010 data-model.md.
/// </summary>
public static class CheckoutStates
{
    public const string Init = "init";
    public const string Addressed = "addressed";
    public const string ShippingSelected = "shipping_selected";
    public const string PaymentSelected = "payment_selected";
    public const string Submitted = "submitted";
    public const string Confirmed = "confirmed";
    public const string Failed = "failed";
    public const string Expired = "expired";

    /// <summary>Allowed transitions per spec 010 data-model.md state machine.</summary>
    public static bool IsValidTransition(string from, string to) => (from, to) switch
    {
        (Init, Addressed) => true,
        (Addressed, ShippingSelected) => true,
        (ShippingSelected, PaymentSelected) => true,
        (PaymentSelected, Submitted) => true,
        (Submitted, Confirmed) => true,
        (Submitted, Failed) => true,
        (Failed, PaymentSelected) => true,            // customer retries with new method
        // Pre-submit → expired (worker or admin). `Submitted` is NOT expirable — once the
        // payment call is underway we let it finish or fail explicitly.
        (Init, Expired) => true,
        (Addressed, Expired) => true,
        (ShippingSelected, Expired) => true,
        (PaymentSelected, Expired) => true,
        // Address changes reset downstream selections (spec edge case 5: address unserviceable).
        (ShippingSelected, Addressed) => true,
        (PaymentSelected, Addressed) => true,
        (PaymentSelected, ShippingSelected) => true,
        _ => false,
    };

    public static bool TryTransition(CheckoutSession session, string target, DateTimeOffset nowUtc)
    {
        if (!IsValidTransition(session.State, target))
        {
            return false;
        }
        session.State = target;
        session.LastTouchedAt = nowUtc;
        session.UpdatedAt = nowUtc;
        switch (target)
        {
            case Submitted: session.SubmittedAt ??= nowUtc; break;
            case Confirmed: session.ConfirmedAt ??= nowUtc; break;
            case Failed: session.FailedAt ??= nowUtc; break;
            case Expired: session.ExpiredAt ??= nowUtc; break;
        }
        return true;
    }
}

/// <summary>PaymentAttempt state machine (spec 010 data-model.md).</summary>
public static class PaymentAttemptStates
{
    public const string Initiated = "initiated";
    public const string Authorized = "authorized";
    public const string Captured = "captured";
    public const string Declined = "declined";
    public const string Voided = "voided";
    public const string Failed = "failed";
    public const string Refunded = "refunded";
    public const string PendingWebhook = "pending_webhook";

    /// <summary>
    /// Allowed PaymentAttempt transitions per spec 010 data-model.md. CR review on PR #30
    /// round 2 — webhooks must route through this helper so a late / out-of-order delivery
    /// can't silently move a terminal attempt backward.
    /// </summary>
    public static bool IsValidTransition(string from, string to) => (from, to) switch
    {
        (Initiated, Authorized) => true,
        (Initiated, Captured) => true,            // CapturedSynchronously path
        (Initiated, PendingWebhook) => true,
        (Initiated, Declined) => true,
        (Initiated, Failed) => true,
        (Initiated, Voided) => true,              // saga compensation pre-capture
        (Authorized, Captured) => true,
        (Authorized, Voided) => true,
        (Authorized, Failed) => true,
        (Authorized, PendingWebhook) => true,
        (Captured, Refunded) => true,
        (PendingWebhook, Authorized) => true,
        (PendingWebhook, Captured) => true,
        (PendingWebhook, Declined) => true,
        (PendingWebhook, Failed) => true,
        (PendingWebhook, Voided) => true,
        // Idempotent self-transitions tolerate retried webhooks landing the same state twice.
        (var f, var t) when f == t => true,
        _ => false,
    };

    /// <summary>
    /// Apply a transition iff it's allowed. Returns false to let the caller log + skip rather
    /// than throwing (webhooks always 200 to avoid provider retry storms).
    /// </summary>
    public static bool TryTransition(BackendApi.Modules.Checkout.Entities.PaymentAttempt attempt, string target, DateTimeOffset nowUtc)
    {
        if (!IsValidTransition(attempt.State, target)) return false;
        attempt.State = target;
        attempt.UpdatedAt = nowUtc;
        return true;
    }
}
