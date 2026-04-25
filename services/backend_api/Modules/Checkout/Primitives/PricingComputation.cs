using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Pricing.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Checkout.Primitives;

/// <summary>
/// Shared pricing helper used by Summary + Submit. Pulls the cart + product metadata,
/// invokes spec 007-a's pipeline in the requested mode, and returns a `DriftDetector.PricingSnapshot`
/// plus the per-line price result so the caller can snapshot order lines on submit.
/// </summary>
public static class PricingComputation
{
    public sealed record Outcome(
        DriftDetector.PricingSnapshot Snapshot,
        IReadOnlyList<PriceLine> PerLine,
        Guid? ExplanationId,
        string? PricingError);

    public sealed record PriceLine(
        Guid ProductId,
        int Qty,
        long ListMinor,
        long NetMinor,
        long TaxMinor,
        long GrossMinor,
        Guid? ReservationId);

    public static Task<Outcome> RunPreviewAsync(
        CartDbContext cartDb,
        CatalogDbContext catalogDb,
        IPriceCalculator priceCalculator,
        CheckoutSession session,
        CancellationToken ct)
        => RunAsync(cartDb, catalogDb, priceCalculator, session, PricingMode.Preview, orderId: null, ct);

    /// <summary>
    /// Runs pricing in Issue mode against a pre-allocated orderId. Pricing's Issue gate
    /// (`pricing.issue_requires_owner`) demands a stable owner id so explanations aren't
    /// orphaned — Checkout generates the id upfront and hands it to the order handler after
    /// Issue completes, keeping the explanation row bound to the future order.
    /// </summary>
    public static Task<Outcome> RunIssueAsync(
        CartDbContext cartDb,
        CatalogDbContext catalogDb,
        IPriceCalculator priceCalculator,
        CheckoutSession session,
        Guid orderId,
        CancellationToken ct)
        => RunAsync(cartDb, catalogDb, priceCalculator, session, PricingMode.Issue, orderId, ct);

    private static async Task<Outcome> RunAsync(
        CartDbContext cartDb,
        CatalogDbContext catalogDb,
        IPriceCalculator priceCalculator,
        CheckoutSession session,
        PricingMode mode,
        Guid? orderId,
        CancellationToken ct)
    {
        var lines = await cartDb.CartLines.AsNoTracking()
            .Where(l => l.CartId == session.CartId)
            .OrderBy(l => l.AddedAt)
            .ToListAsync(ct);
        var productIds = lines.Select(l => l.ProductId).Distinct().ToArray();
        var products = await catalogDb.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(ct);
        var byId = products.ToDictionary(p => p.Id);
        var productCategories = await catalogDb.ProductCategories.AsNoTracking()
            .Where(pc => productIds.Contains(pc.ProductId))
            .ToListAsync(ct);

        var currency = PricingConstants.ResolveCurrency(session.MarketCode);
        var pricingLines = new List<PricingContextLine>(lines.Count);
        foreach (var l in lines)
        {
            // CR review on PR #30: silently skipping lines whose product is missing or has no
            // price hint lets Submit succeed with a partial cart. Treat both as a hard pricing
            // failure so the caller surfaces the gap to the customer instead of dropping items.
            if (!byId.TryGetValue(l.ProductId, out var product))
            {
                var fallback = new DriftDetector.PricingSnapshot(0, 0, 0, 0, currency, session.CouponCode, Array.Empty<DriftDetector.LineSnapshot>());
                return new Outcome(fallback, Array.Empty<PriceLine>(), null,
                    $"pricing.line_product_missing:{l.ProductId}");
            }
            if (product.PriceHintMinorUnits is null)
            {
                var fallback = new DriftDetector.PricingSnapshot(0, 0, 0, 0, currency, session.CouponCode, Array.Empty<DriftDetector.LineSnapshot>());
                return new Outcome(fallback, Array.Empty<PriceLine>(), null,
                    $"pricing.line_unpriceable:{l.ProductId}");
            }
            var cats = productCategories.Where(pc => pc.ProductId == l.ProductId).Select(pc => pc.CategoryId).ToArray();
            pricingLines.Add(new PricingContextLine(
                ProductId: l.ProductId,
                Qty: l.Qty,
                ListPriceMinor: product.PriceHintMinorUnits.Value,
                Restricted: product.Restricted,
                CategoryIds: cats));
        }

        if (pricingLines.Count == 0)
        {
            var empty = new DriftDetector.PricingSnapshot(0, 0, 0, 0, currency, session.CouponCode, Array.Empty<DriftDetector.LineSnapshot>());
            return new Outcome(empty, Array.Empty<PriceLine>(), null, null);
        }

        var ctx = new PricingContext(
            MarketCode: session.MarketCode,
            Locale: "en",
            Account: session.AccountId is { } aid ? new PricingAccountContext(aid, TierSlug: null, VerificationState: "unknown") : null,
            Lines: pricingLines,
            CouponCode: session.CouponCode,
            QuotationId: null,
            OrderId: orderId,
            NowUtc: DateTimeOffset.UtcNow,
            Mode: mode);

        var outcome = await priceCalculator.CalculateAsync(ctx, ct);
        if (!outcome.IsSuccess)
        {
            var fallback = new DriftDetector.PricingSnapshot(0, 0, 0, 0, currency, session.CouponCode, Array.Empty<DriftDetector.LineSnapshot>());
            return new Outcome(fallback, Array.Empty<PriceLine>(), null, outcome.ReasonCode ?? "pricing.failed");
        }
        var r = outcome.Result!;
        var perLine = r.Lines.Select(pl =>
        {
            var cartLine = lines.First(l => l.ProductId == pl.ProductId);
            return new PriceLine(pl.ProductId, pl.Qty, pl.ListMinor * pl.Qty, pl.NetMinor, pl.TaxMinor, pl.GrossMinor, cartLine.ReservationId);
        }).ToArray();
        var snapshot = new DriftDetector.PricingSnapshot(
            r.Totals.SubtotalMinor, r.Totals.DiscountMinor, r.Totals.TaxMinor, r.Totals.GrandTotalMinor,
            currency, session.CouponCode,
            perLine.Select(p => new DriftDetector.LineSnapshot(p.ProductId, p.Qty, p.NetMinor, p.GrossMinor)).ToArray());
        return new Outcome(snapshot, perLine, r.ExplanationId, null);
    }
}
