using BackendApi.Modules.Payments.Providers;

namespace BackendApi.Modules.Payments.PciScope;

/// <summary>
/// BR-14 / V-8 — enforces the minimum-sufficient field set for any provider
/// egress call. The allow-list lives here, in one CODEOWNERS-protected file,
/// so changes require <c>@payments-team</c> AND <c>@security-team</c> review.
///
/// Any provider implementation that needs to add fields to the egress payload
/// must update this allow-list. The PII-egress sweep test (T063) instantiates
/// real provider dispatches and asserts every key passes through this filter.
///
/// The filter operates on the <see cref="CreatePaymentDispatch"/> record's
/// public field set + a small set of derived payload keys (each provider
/// declares its derived-key set). Failure → throw — never silently strip.
/// </summary>
public static class EgressPayloadFilter
{
    /// <summary>Keys allowed on the canonical create-payment dispatch payload.</summary>
    public static readonly IReadOnlySet<string> AllowedCreatePaymentKeys =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "payment_id",
            "order_id",
            "market_code",
            "method",
            "amount",
            "currency",
            "idempotency_key",
            "customer_ref",
            "recipient_name_redacted",
            "recipient_phone_masked_last4",
            // Provider-derived keys — kept narrow per BR-14.
            "merchant_ref",
            "return_url",
            "webhook_url",
            "locale",
        };

    /// <summary>Keys allowed on the canonical refund-dispatch payload.</summary>
    public static readonly IReadOnlySet<string> AllowedRefundKeys =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "refund_id",
            "provider_message_id",
            "amount",
            "currency",
            "reason",
        };

    /// <summary>
    /// Validates a dict of payload keys against the create-payment allow-list.
    /// Throws <see cref="EgressPayloadFilterViolation"/> on any forbidden key
    /// — fail-closed so accidental PII never leaves the runtime.
    /// </summary>
    public static void EnsureCreatePaymentPayloadAllowed(IEnumerable<string> payloadKeys, string providerId)
    {
        var forbidden = payloadKeys
            .Where(k => !AllowedCreatePaymentKeys.Contains(k))
            .ToList();

        if (forbidden.Count > 0)
        {
            throw new EgressPayloadFilterViolation(providerId, forbidden);
        }
    }

    public static void EnsureRefundPayloadAllowed(IEnumerable<string> payloadKeys, string providerId)
    {
        var forbidden = payloadKeys
            .Where(k => !AllowedRefundKeys.Contains(k))
            .ToList();

        if (forbidden.Count > 0)
        {
            throw new EgressPayloadFilterViolation(providerId, forbidden);
        }
    }
}

/// <summary>Thrown when a provider impl would have shipped a non-allow-listed field.</summary>
public sealed class EgressPayloadFilterViolation : Exception
{
    public string ProviderId { get; }
    public IReadOnlyList<string> ForbiddenKeys { get; }

    public EgressPayloadFilterViolation(string providerId, IReadOnlyList<string> forbiddenKeys)
        : base($"Egress payload for provider '{providerId}' contains forbidden keys: {string.Join(", ", forbiddenKeys)}")
    {
        ProviderId = providerId;
        ForbiddenKeys = forbiddenKeys;
    }
}
