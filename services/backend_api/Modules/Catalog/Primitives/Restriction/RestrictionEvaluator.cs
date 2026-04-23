using BackendApi.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Primitives.Restriction;

public sealed class RestrictionEvaluator(CatalogDbContext dbContext, RestrictionCache cache)
{
    private readonly CatalogDbContext _dbContext = dbContext;
    private readonly RestrictionCache _cache = cache;

    public async Task<RestrictionDecision> CheckAsync(
        Guid productId,
        string marketCode,
        string verificationState,
        CancellationToken cancellationToken)
    {
        var normalizedMarket = marketCode.Trim().ToLowerInvariant();
        var normalizedVerification = verificationState.Trim().ToLowerInvariant();

        if (_cache.TryGet(productId, normalizedMarket, normalizedVerification, out var cached))
        {
            return cached;
        }

        var product = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new
            {
                p.Restricted,
                p.RestrictionReasonCode,
                p.RestrictionMarkets,
            })
            .SingleOrDefaultAsync(cancellationToken);

        RestrictionDecision decision;
        if (product is null)
        {
            decision = new RestrictionDecision(false, "catalog.product.not_found");
        }
        else if (!product.Restricted)
        {
            decision = new RestrictionDecision(true, "ok");
        }
        else
        {
            var appliesToMarket = product.RestrictionMarkets.Length == 0
                || product.RestrictionMarkets.Contains(normalizedMarket, StringComparer.OrdinalIgnoreCase);

            if (!appliesToMarket)
            {
                decision = new RestrictionDecision(true, "ok");
            }
            else if (string.Equals(product.RestrictionReasonCode, "professional_verification", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(normalizedVerification, "verified", StringComparison.OrdinalIgnoreCase))
            {
                decision = new RestrictionDecision(true, "ok");
            }
            else
            {
                var reason = string.IsNullOrWhiteSpace(product.RestrictionReasonCode)
                    ? "catalog.restricted.verification_required"
                    : $"catalog.restricted.{product.RestrictionReasonCode}";
                decision = new RestrictionDecision(false, reason);
            }
        }

        _cache.Set(productId, normalizedMarket, normalizedVerification, decision);
        return decision;
    }
}

public sealed record RestrictionDecision(bool Allowed, string ReasonCode);
