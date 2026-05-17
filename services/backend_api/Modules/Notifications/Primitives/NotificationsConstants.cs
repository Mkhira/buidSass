namespace BackendApi.Modules.Notifications.Primitives;

/// <summary>
/// Closed-set string enums for the Notifications module per spec 025
/// (Phase 1E · Milestone 8). Centralized so EF check constraints, state
/// machines, validators, and provider code all reference the same vocabulary.
/// </summary>
public static class NotificationsConstants
{
    public static class Markets
    {
        public const string Sa = "sa";
        public const string Eg = "eg";
        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Sa, Eg };
    }

    public static class Locales
    {
        public const string Ar = "ar";
        public const string En = "en";
        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Ar, En };
    }

    public static class Channels
    {
        public const string Sms = "sms";
        public const string Email = "email";
        public const string Push = "push";
        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Sms, Email, Push };
    }

    public static class Categories
    {
        public const string Transactional = "transactional";
        public const string Marketing = "marketing";
        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Transactional, Marketing };
    }

    public static class RecipientKinds
    {
        public const string Customer = "customer";
        public const string Admin = "admin";
        public const string Anonymous = "anonymous";
        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Customer, Admin, Anonymous };
    }

    /// <summary>Notification.state — per data-model.md state-machine.</summary>
    public static class NotificationStates
    {
        public const string Pending = "pending";
        public const string Queued = "queued";
        public const string Sending = "sending";
        public const string Delivered = "delivered";
        public const string Failed = "failed";
        public const string Retrying = "retrying";
        public const string DeadLetter = "dead_letter";
        public const string Skipped = "skipped";

        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
            { Pending, Queued, Sending, Delivered, Failed, Retrying, DeadLetter, Skipped };
    }

    /// <summary>TemplateVersion.state — per data-model.md.</summary>
    public static class TemplateVersionStates
    {
        public const string Draft = "draft";
        public const string InReview = "in_review";
        public const string Published = "published";
        public const string Archived = "archived";

        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
            { Draft, InReview, Published, Archived };
    }

    /// <summary>Campaign.state — per data-model.md.</summary>
    public static class CampaignStates
    {
        public const string Draft = "draft";
        public const string Scheduled = "scheduled";
        public const string Sending = "sending";
        public const string Paused = "paused";
        public const string Completed = "completed";
        public const string Cancelled = "cancelled";

        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
            { Draft, Scheduled, Sending, Paused, Completed, Cancelled };
    }

    public static class SkippedReasons
    {
        public const string ChannelDisabledByCustomer = "channel_disabled_by_customer";
        public const string RateLimited = "rate_limited";
        public const string RecipientDeactivated = "recipient_deactivated";
        public const string QuietHours = "quiet_hours";
        public const string OptedOut = "opted_out";
        public const string PushTokenInvalid = "push_token_invalid";
        public const string EmailHardBounce = "email_hard_bounce";
    }

    public static class Providers
    {
        public const string Ses = "ses";
        public const string SendGrid = "sendgrid";
        public const string Unifonic = "unifonic";
        public const string VodafoneEgypt = "vodafone-egypt";
        public const string Infobip = "infobip";
        public const string Fcm = "fcm";

        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
            { Ses, SendGrid, Unifonic, VodafoneEgypt, Infobip, Fcm };
    }

    /// <summary>Hangfire queue names per BR-15 OTP isolation.</summary>
    public static class Queues
    {
        public const string OtpPriority = "otp-priority";
        public const string Default = "default";
    }

    public static class EventKinds
    {
        public const string AuthOtpRequested = "auth.otp_requested";
        public const string OrderPlaced = "order.placed";
        public const string OrderConfirmed = "order.confirmed";
        public const string OrderShipped = "order.shipped";
        public const string OrderDelivered = "order.delivered";
        public const string OrderCancelled = "order.cancelled";
        public const string OrderRefundInitiated = "order.refund_initiated";
        public const string OrderRefundCompleted = "order.refund_completed";
        public const string VerificationApproved = "verification.approved";
        public const string VerificationRejected = "verification.rejected";
        public const string PricingPriceDropped = "pricing.price_dropped";
        public const string InventoryRestocked = "inventory.restocked";
        public const string CartAbandoned24h = "cart.abandoned_24h";
        public const string ShippingStatusChanged = "shipping.status_changed";
    }
}
