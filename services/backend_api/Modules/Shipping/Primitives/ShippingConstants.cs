namespace BackendApi.Modules.Shipping.Primitives;

/// <summary>
/// Canonical strings + ranges referenced across slices. Sourced from
/// <c>specs/phase-1E/026-shipping/data-model.md</c> and <c>spec.md</c>.
/// Kept dependency-free so EF configurations + handlers + tests can all
/// import without dragging the runtime graph.
/// </summary>
public static class ShippingConstants
{
    /// <summary>Per-market codes recognized by the Shipping module (per Principle 5).</summary>
    public static class Markets
    {
        public const string SA = "SA";
        public const string EG = "EG";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal) { SA, EG };

        public static bool IsValid(string? code) =>
            code is not null && All.Contains(code);
    }

    /// <summary>Provider identifiers used in routing + KV slot lookup (per spec.md §clarifications).</summary>
    public static class Providers
    {
        public const string Smsa = "smsa";
        public const string AramexKsa = "aramex-ksa";
        public const string AramexEg = "aramex-eg";
        public const string Bosta = "bosta";

        public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByMarket =
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                [Markets.SA] = new HashSet<string>(StringComparer.Ordinal) { Smsa, AramexKsa },
                [Markets.EG] = new HashSet<string>(StringComparer.Ordinal) { Bosta, AramexEg },
            };

        public static bool SupportsMarket(string providerId, string marketCode) =>
            ByMarket.TryGetValue(marketCode, out var set) && set.Contains(providerId);
    }

    /// <summary>Currency code per market (per data-model.md §market_schemas).</summary>
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

    /// <summary>Audit-event action strings emitted by the Shipping module (per data-model.md §audit).</summary>
    public static class AuditActions
    {
        public const string MethodPublished = "shipping.method_published";
        public const string MethodArchived = "shipping.method_archived";
        public const string FeeTableUpdated = "shipping.fee_table_updated";
        public const string LabelPurchased = "shipping.label_purchased";
        public const string HandedToCarrier = "shipping.handed_to_carrier";
        public const string StatusChanged = "shipping.status_changed";
        public const string DeliveryDisputed = "shipping.delivery_disputed";
        public const string ReDeliveryCreated = "shipping.re_delivery_created";
        public const string LabelVoided = "shipping.label_voided";
        public const string LabelCreationFailed = "shipping.label_creation_failed";
        public const string DeadLetter = "shipping.dead_letter";
        public const string SlaBreach = "shipping.sla_breach";
        public const string ProviderDegraded = "provider.degraded";
        public const string ProviderFailover = "provider.failover";
        public const string SecretPlaceholderReplaced = "secret.placeholder_replaced";
    }

    /// <summary>Entity-type strings used in the audit-log row (per spec 003 audit_log schema).</summary>
    public static class EntityTypes
    {
        public const string ShippingMethodVersion = "shipping.method_version";
        public const string FeeTable = "shipping.fee_table";
        public const string Shipment = "shipping.shipment";
        public const string ProviderRouting = "shipping.provider_routing";
        public const string DeadLetterLabel = "shipping.dead_letter_label";
        public const string KvSecret = "secret.placeholder";
    }
}
