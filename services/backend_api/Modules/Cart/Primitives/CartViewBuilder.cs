using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Primitives;

public sealed record CartLineView(
    Guid Id,
    Guid ProductId,
    int Qty,
    bool Restricted,
    string? RestrictionReasonCode,
    bool Unavailable,
    bool StockChanged,
    CartLinePriceBreakdown PriceBreakdown);

public sealed record CartLinePriceBreakdown(
    long ListMinor,
    long NetMinor,
    long TaxMinor,
    long GrossMinor,
    IReadOnlyList<PriceLayerView> Layers);

public sealed record PriceLayerView(string Layer, string? RuleId, string? RuleKind, long AppliedMinor);

public sealed record CartPricingView(
    string Currency,
    long SubtotalMinor,
    long DiscountMinor,
    long TaxMinor,
    long GrandTotalMinor,
    string? Error);

public sealed record CartB2BView(
    string? PoNumber,
    string? Reference,
    string? Notes,
    DateTimeOffset? RequestedDeliveryFrom,
    DateTimeOffset? RequestedDeliveryTo);

public sealed record CartSavedItemView(Guid ProductId, int Qty);

public sealed record CheckoutEligibilityView(bool Allowed, string? ReasonCode);

public sealed record CartView(
    Guid Id,
    string MarketCode,
    string Status,
    IReadOnlyList<CartLineView> Lines,
    IReadOnlyList<CartSavedItemView> SavedItems,
    string? CouponCode,
    CartPricingView Pricing,
    CheckoutEligibilityView CheckoutEligibility,
    CartB2BView B2b);

/// <summary>
/// Builds the wire-shape `CartView` for every cart read/mutation response. On each call it
/// runs pricing in Preview mode (spec 007-a) so the cart response carries no stored totals
/// and cannot drift from the pricing rules (R3 / FR-005).
/// </summary>
public sealed class CartViewBuilder(
    IPriceCalculator priceCalculator,
    EligibilityEvaluator eligibilityEvaluator)
{
    public async Task<CartView> BuildAsync(
        Entities.Cart cart,
        IReadOnlyList<CartLine> lines,
        IReadOnlyList<CartSavedItem> savedItems,
        CartB2BMetadata? b2bMetadata,
        CatalogDbContext catalogDb,
        bool customerVerifiedForRestriction,
        bool customerIsB2B,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var currency = PricingConstants.ResolveCurrency(cart.MarketCode);
        var savedViews = savedItems.Select(s => new CartSavedItemView(s.ProductId, s.Qty)).ToArray();

        if (lines.Count == 0)
        {
            return new CartView(
                Id: cart.Id,
                MarketCode: cart.MarketCode,
                Status: cart.Status,
                Lines: Array.Empty<CartLineView>(),
                SavedItems: savedViews,
                CouponCode: cart.CouponCode,
                Pricing: new CartPricingView(currency, 0, 0, 0, 0, null),
                CheckoutEligibility: new CheckoutEligibilityView(false, "cart.empty"),
                B2b: BuildB2B(b2bMetadata));
        }

        var productIds = lines.Select(l => l.ProductId).Distinct().ToArray();
        var products = await catalogDb.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        var productById = products.ToDictionary(p => p.Id);

        var productCategories = await catalogDb.ProductCategories
            .AsNoTracking()
            .Where(pc => productIds.Contains(pc.ProductId))
            .ToListAsync(cancellationToken);

        var pricingLines = new List<PricingContextLine>(lines.Count);
        foreach (var line in lines)
        {
            if (!productById.TryGetValue(line.ProductId, out var product)
                || product.PriceHintMinorUnits is null
                || !product.MarketCodes.Any(m => string.Equals(m, cart.MarketCode, StringComparison.OrdinalIgnoreCase))
                || string.Equals(product.Status, "archived", StringComparison.OrdinalIgnoreCase))
            {
                // product went away / archived / wrong market — skip pricing but leave the line
                // flagged as unavailable. FR-022.
                continue;
            }
            var categoryIds = productCategories
                .Where(pc => pc.ProductId == line.ProductId)
                .Select(pc => pc.CategoryId)
                .ToArray();
            pricingLines.Add(new PricingContextLine(
                ProductId: line.ProductId,
                Qty: line.Qty,
                ListPriceMinor: product.PriceHintMinorUnits.Value,
                Restricted: product.Restricted,
                CategoryIds: categoryIds));
        }

        PriceResult? priceResult = null;
        string? pricingError = null;
        if (pricingLines.Count > 0)
        {
            var ctx = new PricingContext(
                MarketCode: cart.MarketCode,
                Locale: "en",
                Account: cart.AccountId is { } aid ? new PricingAccountContext(aid, TierSlug: null, VerificationState: "unknown") : null,
                Lines: pricingLines,
                CouponCode: cart.CouponCode,
                QuotationId: null,
                OrderId: null,
                NowUtc: nowUtc,
                Mode: PricingMode.Preview);
            var outcome = await priceCalculator.CalculateAsync(ctx, cancellationToken);
            if (outcome.IsSuccess)
            {
                priceResult = outcome.Result;
            }
            else
            {
                pricingError = outcome.ReasonCode ?? "pricing.preview_failed";
            }
        }

        var pricedLinesById = priceResult?.Lines.ToDictionary(l => l.ProductId) ?? new();
        var lineViews = new List<CartLineView>(lines.Count);
        var hasRestricted = false;
        var hasUnavailable = false;
        var hasB2BOnly = false;
        string? firstRestrictionReason = null;

        foreach (var line in lines)
        {
            productById.TryGetValue(line.ProductId, out var product);
            var productExists = product is not null;
            var productArchived = productExists && string.Equals(product!.Status, "archived", StringComparison.OrdinalIgnoreCase);
            var productInMarket = productExists && product!.MarketCodes.Any(m => string.Equals(m, cart.MarketCode, StringComparison.OrdinalIgnoreCase));
            var unavailable = line.Unavailable || !productExists || productArchived || !productInMarket;
            var restricted = line.Restricted || (productExists && product!.Restricted);
            var restrictedReason = line.RestrictionReasonCode
                ?? (productExists ? product!.RestrictionReasonCode : null);

            if (restricted) hasRestricted = true;
            if (unavailable) hasUnavailable = true;
            if (restricted && firstRestrictionReason is null)
            {
                firstRestrictionReason = restrictedReason;
            }
            // B2B-only flag currently routes through the restriction_reason_code vocabulary —
            // `catalog.restricted.business_only` is the agreed catalog signal. EligibilityEvaluator
            // then forks B2B vs generic restriction based on the reason code's semantic family.
            if (restricted && string.Equals(restrictedReason, "catalog.restricted.business_only", StringComparison.OrdinalIgnoreCase))
            {
                hasB2BOnly = true;
            }

            long listMinor = 0, netMinor = 0, taxMinor = 0, grossMinor = 0;
            IReadOnlyList<PriceLayerView> layers = Array.Empty<PriceLayerView>();
            if (pricedLinesById.TryGetValue(line.ProductId, out var priced))
            {
                listMinor = priced.ListMinor * priced.Qty;
                netMinor = priced.NetMinor;
                taxMinor = priced.TaxMinor;
                grossMinor = priced.GrossMinor;
                layers = priced.Layers
                    .Select(r => new PriceLayerView(r.Layer, r.RuleId, r.RuleKind, r.AppliedMinor))
                    .ToArray();
            }

            lineViews.Add(new CartLineView(
                Id: line.Id,
                ProductId: line.ProductId,
                Qty: line.Qty,
                Restricted: restricted,
                RestrictionReasonCode: restrictedReason,
                Unavailable: unavailable,
                StockChanged: line.StockChanged,
                PriceBreakdown: new CartLinePriceBreakdown(listMinor, netMinor, taxMinor, grossMinor, layers)));
        }

        var pricing = priceResult is null
            ? new CartPricingView(currency, 0, 0, 0, 0, pricingError)
            : new CartPricingView(
                currency,
                priceResult.Totals.SubtotalMinor,
                priceResult.Totals.DiscountMinor,
                priceResult.Totals.TaxMinor,
                priceResult.Totals.GrandTotalMinor,
                null);

        var eligibility = eligibilityEvaluator.Evaluate(new EligibilityEvaluator.Input(
            HasAnyRestrictedLine: hasRestricted,
            RestrictedReasonCode: firstRestrictionReason,
            CustomerVerifiedForRestriction: customerVerifiedForRestriction,
            HasAnyUnavailableLine: hasUnavailable,
            HasAnyInventoryShortfall: false,
            HasAnyB2BOnlyLine: hasB2BOnly,
            CustomerIsB2B: customerIsB2B,
            LineCount: lineViews.Count));

        return new CartView(
            Id: cart.Id,
            MarketCode: cart.MarketCode,
            Status: cart.Status,
            Lines: lineViews,
            SavedItems: savedViews,
            CouponCode: cart.CouponCode,
            Pricing: pricing,
            CheckoutEligibility: new CheckoutEligibilityView(eligibility.Allowed, eligibility.ReasonCode),
            B2b: BuildB2B(b2bMetadata));
    }

    private static CartB2BView BuildB2B(CartB2BMetadata? m) => m is null
        ? new CartB2BView(null, null, null, null, null)
        : new CartB2BView(m.PoNumber, m.Reference, m.Notes, m.RequestedDeliveryFrom, m.RequestedDeliveryTo);
}
