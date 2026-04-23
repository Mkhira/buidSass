using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Customer.PriceCart;

public static class PriceCartHandler
{
    public static async Task<PriceCartHandlerResult> HandleAsync(
        PriceCartRequest request,
        IPriceCalculator calculator,
        CatalogDbContext catalogDb,
        PricingDbContext pricingDb,
        DateTimeOffset nowUtc,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return PriceCartHandlerResult.Fail(400, "pricing.lines_required", "At least one line is required.");
        }

        var marketCode = request.MarketCode?.Trim().ToLowerInvariant() ?? PricingConstants.DefaultMarketCode;
        var locale = request.Locale?.Trim().ToLowerInvariant() ?? "en";

        if (marketCode is not ("ksa" or "eg"))
        {
            return PriceCartHandlerResult.Fail(400, "pricing.currency_mismatch", "Unknown market.");
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToArray();
        var products = await catalogDb.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.Status == "published")
            .ToListAsync(cancellationToken);

        var byId = products.ToDictionary(p => p.Id);
        foreach (var line in request.Lines)
        {
            if (!byId.TryGetValue(line.ProductId, out var product))
            {
                return PriceCartHandlerResult.Fail(404, "pricing.product.not_found", $"Product {line.ProductId:N} not found.");
            }
            if (!product.MarketCodes.Any(m => string.Equals(m, marketCode, StringComparison.OrdinalIgnoreCase)))
            {
                return PriceCartHandlerResult.Fail(400, "pricing.currency_mismatch", $"Product {line.ProductId:N} not sold in market {marketCode}.");
            }
            if (product.PriceHintMinorUnits is null)
            {
                return PriceCartHandlerResult.Fail(400, "pricing.product.no_price", $"Product {line.ProductId:N} has no price hint.");
            }
            if (line.Qty < 1)
            {
                return PriceCartHandlerResult.Fail(400, "pricing.invalid_qty", "Line qty must be >= 1.");
            }
        }

        var catalogCategories = await catalogDb.ProductCategories
            .AsNoTracking()
            .Where(pc => productIds.Contains(pc.ProductId))
            .ToListAsync(cancellationToken);

        PricingAccountContext? accountCtx = null;
        if (accountId is Guid aid)
        {
            var assignment = await pricingDb.AccountB2BTiers
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.AccountId == aid, cancellationToken);
            string? tierSlug = null;
            if (assignment is not null)
            {
                tierSlug = await pricingDb.B2BTiers
                    .AsNoTracking()
                    .Where(t => t.Id == assignment.TierId && t.IsActive)
                    .Select(t => t.Slug)
                    .SingleOrDefaultAsync(cancellationToken);
            }
            accountCtx = new PricingAccountContext(aid, tierSlug, VerificationState: "unknown");
        }

        var ctxLines = request.Lines.Select(l =>
        {
            var product = byId[l.ProductId];
            var cats = catalogCategories.Where(pc => pc.ProductId == l.ProductId).Select(pc => pc.CategoryId).ToArray();
            return new PricingContextLine(
                ProductId: l.ProductId,
                Qty: l.Qty,
                ListPriceMinor: product.PriceHintMinorUnits!.Value,
                Restricted: product.Restricted,
                CategoryIds: cats);
        }).ToArray();

        var ctx = new PricingContext(
            MarketCode: marketCode,
            Locale: locale,
            Account: accountCtx,
            Lines: ctxLines,
            CouponCode: request.CouponCode,
            QuotationId: null,
            OrderId: null,
            NowUtc: nowUtc,
            Mode: PricingMode.Preview);

        var outcome = await calculator.CalculateAsync(ctx, cancellationToken);
        if (!outcome.IsSuccess)
        {
            return PriceCartHandlerResult.Fail(outcome.StatusCode, outcome.ReasonCode!, outcome.Detail!);
        }

        var result = outcome.Result!;
        var response = new PriceCartResponse(
            Lines: result.Lines.Select(l => new PriceCartResponseLine(
                l.ProductId,
                l.Qty,
                l.ListMinor,
                l.NetMinor,
                l.TaxMinor,
                l.GrossMinor,
                l.Layers.Select(r => new PriceCartLayer(r.Layer, r.RuleId, r.RuleKind, r.AppliedMinor)).ToArray()))
                .ToArray(),
            Totals: new PriceCartTotals(
                result.Totals.SubtotalMinor,
                result.Totals.DiscountMinor,
                result.Totals.TaxMinor,
                result.Totals.GrandTotalMinor),
            Currency: result.Currency,
            ExplanationHash: result.ExplanationHash);

        return PriceCartHandlerResult.Success(response);
    }
}

public sealed record PriceCartHandlerResult(
    bool IsSuccess,
    PriceCartResponse? Response,
    int StatusCode,
    string? ReasonCode,
    string? Detail)
{
    public static PriceCartHandlerResult Success(PriceCartResponse response) =>
        new(true, response, 200, null, null);
    public static PriceCartHandlerResult Fail(int status, string reason, string detail) =>
        new(false, null, status, reason, detail);
}
