namespace BackendApi.Modules.Payments.Primitives;

/// <summary>
/// Canonical strings + closed value sets for the Payments module per spec 027.
/// Kept dependency-free so EF configurations, handlers, providers, and tests
/// can all import without dragging the runtime graph.
/// </summary>
public static class PaymentsConstants
{
    /// <summary>Per-market codes recognized by the Payments module (Principle 5).</summary>
    public static class Markets
    {
        public const string SA = "sa";
        public const string EG = "eg";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal) { SA, EG };

        public static bool IsValid(string? code) =>
            code is not null && All.Contains(code);
    }

    /// <summary>Provider identifiers used in routing + KV slot lookup (ADR-007).</summary>
    public static class Providers
    {
        public const string HyperPay = "hyperpay";
        public const string Tap = "tap";
        public const string Paymob = "paymob";
        public const string Kashier = "kashier";
        public const string Tabby = "tabby";
        public const string Tamara = "tamara";
        public const string Valu = "valu";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal) { HyperPay, Tap, Paymob, Kashier, Tabby, Tamara, Valu };

        public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByMarket =
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                [Markets.SA] = new HashSet<string>(StringComparer.Ordinal) { HyperPay, Tap, Tabby, Tamara },
                [Markets.EG] = new HashSet<string>(StringComparer.Ordinal) { Paymob, Kashier, Valu },
            };

        public static bool SupportsMarket(string providerId, string marketCode) =>
            ByMarket.TryGetValue(marketCode, out var set) && set.Contains(providerId);
    }

    /// <summary>Payment method identifiers (V-7 market eligibility enforced separately).</summary>
    public static class Methods
    {
        public const string Card = "card";
        public const string ApplePay = "apple_pay";
        public const string Mada = "mada";
        public const string StcPay = "stc_pay";
        public const string Meeza = "meeza";
        public const string BnplTabby = "bnpl_tabby";
        public const string BnplTamara = "bnpl_tamara";
        public const string BnplValu = "bnpl_valu";
        public const string Cod = "cod";
        public const string BankTransfer = "bank_transfer";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal)
            {
                Card, ApplePay, Mada, StcPay, Meeza,
                BnplTabby, BnplTamara, BnplValu,
                Cod, BankTransfer,
            };

        /// <summary>V-7 — per-market method eligibility (default-on at v1 per spec §clarifications).</summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByMarket =
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                [Markets.SA] = new HashSet<string>(StringComparer.Ordinal)
                {
                    Card, ApplePay, Mada, StcPay,
                    BnplTabby, BnplTamara,
                    Cod, BankTransfer,
                },
                [Markets.EG] = new HashSet<string>(StringComparer.Ordinal)
                {
                    Card, ApplePay, Meeza,
                    BnplValu,
                    Cod, BankTransfer,
                },
            };

        public static bool EligibleForMarket(string method, string marketCode) =>
            ByMarket.TryGetValue(marketCode, out var set) && set.Contains(method);
    }

    /// <summary>Per-market currency code (BR-19 — providers settle in market currency at v1).</summary>
    public static class Currencies
    {
        public const string SAR = "SAR";
        public const string EGP = "EGP";

        public static string For(string marketCode) => marketCode switch
        {
            Markets.SA => SAR,
            Markets.EG => EGP,
            _ => throw new ArgumentOutOfRangeException(nameof(marketCode), marketCode, "Unknown market."),
        };
    }

    /// <summary>Payment state machine values (spec.md §state-machine).</summary>
    public static class PaymentStates
    {
        public const string PendingAuthorization = "pending_authorization";
        public const string PendingExternalRedirect = "pending_external_redirect";
        public const string PendingCollectionOnDelivery = "pending_collection_on_delivery";
        public const string PendingBankTransfer = "pending_bank_transfer";
        public const string Authorized = "authorized";
        public const string CaptureFailed = "capture_failed";
        public const string Captured = "captured";
        public const string Failed = "failed";
        public const string Expired = "expired";
        public const string Refunded = "refunded";
        public const string PartiallyRefunded = "partially_refunded";
        public const string ChargebackReceived = "chargeback_received";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal)
            {
                PendingAuthorization, PendingExternalRedirect,
                PendingCollectionOnDelivery, PendingBankTransfer,
                Authorized, CaptureFailed, Captured,
                Failed, Expired,
                Refunded, PartiallyRefunded, ChargebackReceived,
            };

        public static readonly IReadOnlySet<string> Terminal =
            new HashSet<string>(StringComparer.Ordinal) { Failed, Expired, Refunded, ChargebackReceived };

        public static readonly IReadOnlySet<string> ActivePending =
            new HashSet<string>(StringComparer.Ordinal)
            {
                PendingAuthorization, PendingExternalRedirect,
                PendingCollectionOnDelivery, PendingBankTransfer,
                Authorized, CaptureFailed,
            };
    }

    public static class RefundStates
    {
        public const string Pending = "pending";
        public const string Completed = "completed";
        public const string Failed = "failed";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal) { Pending, Completed, Failed };
    }

    public static class ReconciliationExceptionReasons
    {
        public const string OrphanProviderRow = "orphan_provider_row";
        public const string MissingOnProvider = "missing_on_provider";
        public const string AmountMismatch = "amount_mismatch";
        public const string CurrencyMismatch = "currency_mismatch";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal)
            {
                OrphanProviderRow, MissingOnProvider, AmountMismatch, CurrencyMismatch,
            };
    }

    public static class ReconciliationExceptionStates
    {
        public const string Open = "open";
        public const string Resolved = "resolved";
    }

    public static class ReconciliationExceptionResolutions
    {
        public const string RefundIssued = "refund_issued";
        public const string InternalCorrection = "internal_correction";
        public const string ProviderCorrectionRequested = "provider_correction_requested";
        public const string AcceptedLoss = "accepted_loss";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal)
            {
                RefundIssued, InternalCorrection, ProviderCorrectionRequested, AcceptedLoss,
            };
    }

    /// <summary>Failed-reason canonical codes.</summary>
    public static class FailedReasons
    {
        public const string ProviderUnavailable = "provider_unavailable";
        public const string Declined = "declined";
        public const string BnplDeclined = "bnpl_declined";
        public const string CodCollectionFailed = "cod_collection_failed";
        public const string InvalidInput = "invalid_input";
        public const string SignatureMismatch = "signature_mismatch";
    }

    /// <summary>Expired-reason canonical codes.</summary>
    public static class ExpiredReasons
    {
        public const string RedirectTimeout = "redirect_timeout";
        public const string BankTransferTimeout = "bank_transfer_timeout";
        public const string AuthorizationStale = "authorization_stale";
        public const string CapturedNotResolved = "captured_not_resolved";
    }

    /// <summary>Chargeback status values.</summary>
    public static class ChargebackStatuses
    {
        public const string Received = "received";
        public const string Disputed = "disputed";
        public const string Lost = "lost";
        public const string Won = "won";
        public const string Accepted = "accepted";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal) { Received, Disputed, Lost, Won, Accepted };
    }

    /// <summary>Reconciliation run status values.</summary>
    public static class ReconciliationRunStatuses
    {
        public const string Running = "running";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    /// <summary>COD collection outcome values.</summary>
    public static class CodOutcomes
    {
        public const string Collected = "collected";
        public const string Refused = "refused";
        public const string AddressNotFound = "address_not_found";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal) { Collected, Refused, AddressNotFound };
    }

    /// <summary>PCI scope event kinds (spec data-model.md §pci_scope_events).</summary>
    public static class PciScopeEventKinds
    {
        public const string KvSlotAdded = "kv_slot_added";
        public const string KvSlotRemoved = "kv_slot_removed";
        public const string HostedFieldsDomainChanged = "hosted_fields_domain_changed";
        public const string ProviderAdded = "provider_added";
        public const string ProviderRemoved = "provider_removed";
    }

    /// <summary>Audit action strings emitted by the Payments module (spec §audit).</summary>
    public static class AuditActions
    {
        public const string PaymentCreated = "payment.created";
        public const string PaymentAuthorized = "payment.authorized";
        public const string PaymentCaptured = "payment.captured";
        public const string PaymentFailed = "payment.failed";
        public const string PaymentExpired = "payment.expired";
        public const string PaymentRefunded = "payment.refunded";
        public const string PaymentPartiallyRefunded = "payment.partially_refunded";
        public const string PaymentChargeback = "payment.chargeback";
        public const string PaymentWebhookReplay = "payment.webhook_replay";
        public const string ProviderDegraded = "provider.degraded";
        public const string ProviderFailover = "provider.failover";
        public const string ReconciliationRunStarted = "reconciliation.run_started";
        public const string ReconciliationRunCompleted = "reconciliation.run_completed";
        public const string ReconciliationExceptionOpened = "reconciliation.exception_opened";
        public const string ReconciliationExceptionResolved = "reconciliation.exception_resolved";
        public const string PciScopeConfigChanged = "pci_scope.config_changed";
        public const string SecretPlaceholderReplaced = "secret.placeholder_replaced";
    }

    /// <summary>Default timing windows (per spec.md edge cases + research).</summary>
    public static class Windows
    {
        public static readonly TimeSpan ExternalRedirectTtl = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan AuthorizationPendingTtl = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan StcPayTtl = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan BankTransferTtl = TimeSpan.FromHours(72);
        public static readonly TimeSpan CaptureFailedAutoVoid = TimeSpan.FromHours(24);
        public static readonly int CardSoftRateLimitPerCustomerPer24h = 5;
    }
}
