using System.Text.Json;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Verification.Eligibility;

/// <summary>
/// Spec 020 contracts §4.1 / data-model §4. The single authoritative answer to
/// "may this customer purchase this restricted SKU right now?".
///
/// <para>Algorithm (deterministic for <c>(customerId, customerCurrentMarket, sku, point-in-time)</c>):</para>
/// <list type="number">
///   <item>Resolve the SKU's <see cref="ProductRestrictionPolicy"/>. If the SKU
///         is not restricted in the customer's current market, return
///         <see cref="EligibilityClass.Unrestricted"/> (silent path).</item>
///   <item>PK lookup on <c>verification_eligibility_cache</c> for
///         <c>(customerId, customerCurrentMarket)</c>. Missing row =
///         <see cref="EligibilityReasonCode.VerificationRequired"/>.</item>
///   <item>Map the cache row's <c>EligibilityClass</c> +
///         <c>EligibilityReasonCode</c> into a wire-form
///         <see cref="EligibilityResult"/>.</item>
///   <item>If class is <c>eligible</c>, additionally check that the cached
///         <c>professions</c> set covers the SKU's
///         <see cref="ProductRestrictionPolicy.RequiredProfession"/>. A
///         profession mismatch downgrades to
///         <see cref="EligibilityReasonCode.ProfessionMismatch"/>.</item>
/// </list>
///
/// <para>Latency contract: PK cache lookup + one stub policy call. p95 ≤ 5 ms,
/// p99 ≤ 15 ms (SC-004).</para>
/// </summary>
public sealed class CustomerVerificationEligibilityQuery(
    VerificationDbContext db,
    IProductRestrictionPolicy restrictionPolicy)
    : ICustomerVerificationEligibilityQuery
{
    public async ValueTask<EligibilityResult> EvaluateAsync(
        Guid customerId,
        string customerCurrentMarket,
        string sku,
        CancellationToken cancellationToken)
    {
        var policy = await restrictionPolicy.GetForSkuAsync(sku, cancellationToken);

        // Silent path: SKU not restricted in customer's current market.
        if (!policy.RestrictedInMarkets.Contains(customerCurrentMarket))
        {
            return Build(EligibilityClass.Unrestricted, EligibilityReasonCode.Unrestricted, expiresAt: null);
        }

        var cache = await db.EligibilityCache
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.MarketCode == customerCurrentMarket)
            .Select(c => new CacheRow(c.EligibilityClass, c.ReasonCode, c.ExpiresAt, c.ProfessionsJson))
            .SingleOrDefaultAsync(cancellationToken);

        return EvaluateFromCacheRow(cache, policy);
    }

    public async ValueTask<IReadOnlyDictionary<string, EligibilityResult>> EvaluateManyAsync(
        Guid customerId,
        string customerCurrentMarket,
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken)
    {
        if (skus.Count == 0)
        {
            return new Dictionary<string, EligibilityResult>(0);
        }

        // ONE cache lookup, regardless of SKU count. The cache row is
        // per-(customer, market) — every SKU evaluates against the same row.
        var cache = await db.EligibilityCache
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.MarketCode == customerCurrentMarket)
            .Select(c => new CacheRow(c.EligibilityClass, c.ReasonCode, c.ExpiresAt, c.ProfessionsJson))
            .SingleOrDefaultAsync(cancellationToken);

        // ONE policy lookup per SKU. Spec 005 may later expose a bulk-policy
        // entrypoint; this implementation calls the single-SKU API per the
        // current contract — let spec 005 decide whether to batch internally.
        var results = new Dictionary<string, EligibilityResult>(skus.Count);
        foreach (var sku in skus)
        {
            if (results.ContainsKey(sku))
            {
                continue;
            }
            var policy = await restrictionPolicy.GetForSkuAsync(sku, cancellationToken);
            if (!policy.RestrictedInMarkets.Contains(customerCurrentMarket))
            {
                results[sku] = Build(EligibilityClass.Unrestricted, EligibilityReasonCode.Unrestricted, expiresAt: null);
                continue;
            }
            results[sku] = EvaluateFromCacheRow(cache, policy);
        }
        return results;
    }

    private static EligibilityResult EvaluateFromCacheRow(CacheRow? cache, ProductRestrictionPolicy policy)
    {
        if (cache is null)
        {
            // SKU is restricted in customer's market AND no cache row — the
            // customer has no verification of any kind for this market.
            return Build(EligibilityClass.Ineligible, EligibilityReasonCode.VerificationRequired, expiresAt: null);
        }

        // Wire forms come from EligibilityCacheInvalidator — one of:
        //   eligible | ineligible | unrestricted_only
        // Translate, then refine with profession matching for eligible rows.
        switch (cache.EligibilityClass)
        {
            case "eligible":
                {
                    if (policy.RequiredProfession is { } required
                        && !ProfessionsContain(cache.ProfessionsJson, required))
                    {
                        return Build(EligibilityClass.Ineligible, EligibilityReasonCode.ProfessionMismatch, expiresAt: null);
                    }
                    return Build(EligibilityClass.Eligible, EligibilityReasonCode.Eligible, cache.ExpiresAt);
                }

            case "unrestricted_only":
                // The customer has no verification, but the cache reflects that
                // unrestricted purchases are fine. Restricted SKU in customer's
                // market — fall through to ineligible/required.
                return Build(EligibilityClass.Ineligible, EligibilityReasonCode.VerificationRequired, expiresAt: null);

            case "ineligible":
            default:
                {
                    var reason = ParseReasonCode(cache.ReasonCode) ?? EligibilityReasonCode.VerificationRequired;
                    return Build(EligibilityClass.Ineligible, reason, expiresAt: null);
                }
        }
    }

    private static EligibilityResult Build(
        EligibilityClass cls,
        EligibilityReasonCode reasonCode,
        DateTimeOffset? expiresAt)
        => new(cls, reasonCode, reasonCode.ToIcuKey(), expiresAt);

    private static bool ProfessionsContain(string? professionsJson, string required)
    {
        if (string.IsNullOrWhiteSpace(professionsJson))
        {
            return false;
        }
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(professionsJson);
            return arr is not null && arr.Any(p => string.Equals(p, required, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static EligibilityReasonCode? ParseReasonCode(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire))
        {
            return null;
        }
        return Enum.TryParse<EligibilityReasonCode>(wire, ignoreCase: false, out var parsed) ? parsed : null;
    }

    private sealed record CacheRow(string EligibilityClass, string? ReasonCode, DateTimeOffset? ExpiresAt, string ProfessionsJson);
}
