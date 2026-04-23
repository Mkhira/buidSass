using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Pricing.Internal.Calculate;

public static class CalculateHandler
{
    public static async Task<CalculateHandlerResult> HandleAsync(
        CalculateRequest request,
        IPriceCalculator calculator,
        CatalogDbContext catalogDb,
        PricingDbContext pricingDb,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return CalculateHandlerResult.Fail(400, "pricing.lines_required", "At least one line is required.");
        }

        var marketCode = request.MarketCode?.Trim().ToLowerInvariant() ?? PricingConstants.DefaultMarketCode;
        if (marketCode is not ("ksa" or "eg"))
        {
            return CalculateHandlerResult.Fail(400, "pricing.currency_mismatch", "Unknown market.");
        }

        var mode = (request.Mode?.Trim().ToLowerInvariant()) switch
        {
            "issue" => PricingMode.Issue,
            "preview" or null or "" => PricingMode.Preview,
            _ => (PricingMode?)null,
        };
        if (mode is null)
        {
            return CalculateHandlerResult.Fail(400, "pricing.invalid_mode", "Mode must be preview or issue.");
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
                return CalculateHandlerResult.Fail(404, "pricing.product.not_found", $"Product {line.ProductId:N} not found.");
            }
            if (!product.MarketCodes.Any(m => string.Equals(m, marketCode, StringComparison.OrdinalIgnoreCase)))
            {
                return CalculateHandlerResult.Fail(400, "pricing.currency_mismatch", $"Product {line.ProductId:N} not sold in market {marketCode}.");
            }
            if (product.PriceHintMinorUnits is null)
            {
                return CalculateHandlerResult.Fail(400, "pricing.product.no_price", $"Product {line.ProductId:N} has no price hint.");
            }
            if (line.Qty < 1)
            {
                return CalculateHandlerResult.Fail(400, "pricing.invalid_qty", "Line qty must be >= 1.");
            }
        }

        var catalogCategories = await catalogDb.ProductCategories
            .AsNoTracking()
            .Where(pc => productIds.Contains(pc.ProductId))
            .ToListAsync(cancellationToken);

        PricingAccountContext? accountCtx = null;
        if (request.AccountId is Guid aid)
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
            Locale: (request.Locale?.Trim().ToLowerInvariant() ?? "en"),
            Account: accountCtx,
            Lines: ctxLines,
            CouponCode: request.CouponCode,
            QuotationId: request.QuotationId,
            OrderId: request.OrderId,
            NowUtc: nowUtc,
            Mode: mode.Value);

        var outcome = await calculator.CalculateAsync(ctx, cancellationToken);
        if (!outcome.IsSuccess)
        {
            return CalculateHandlerResult.Fail(outcome.StatusCode, outcome.ReasonCode!, outcome.Detail!);
        }

        var result = outcome.Result!;

        // On Issue mode with a coupon + account, atomically record a redemption row and bump used_count
        // under optimistic concurrency. This is what enforces SC-004 (100 concurrent redeems).
        if (mode == PricingMode.Issue
            && !string.IsNullOrWhiteSpace(request.CouponCode)
            && request.AccountId is Guid accountId)
        {
            var recordOutcome = await TryRecordRedemptionAsync(
                pricingDb,
                request.CouponCode!,
                accountId,
                request.OrderId,
                marketCode,
                nowUtc,
                cancellationToken);
            if (!recordOutcome.IsSuccess)
            {
                return CalculateHandlerResult.Fail(recordOutcome.StatusCode, recordOutcome.ReasonCode!, recordOutcome.Detail!);
            }
        }

        var response = new CalculateResponse(
            Lines: result.Lines.Select(l => new CalculateResponseLine(
                l.ProductId, l.Qty, l.ListMinor, l.NetMinor, l.TaxMinor, l.GrossMinor,
                l.Layers.Select(r => new CalculateLayer(r.Layer, r.RuleId, r.RuleKind, r.AppliedMinor)).ToArray()))
                .ToArray(),
            Totals: new CalculateTotals(result.Totals.SubtotalMinor, result.Totals.DiscountMinor, result.Totals.TaxMinor, result.Totals.GrandTotalMinor),
            Currency: result.Currency,
            ExplanationHash: result.ExplanationHash,
            ExplanationId: result.ExplanationId);

        return CalculateHandlerResult.Success(response);
    }

    private static async Task<CalculateHandlerResult> TryRecordRedemptionAsync(
        PricingDbContext db,
        string couponCode,
        Guid accountId,
        Guid? orderId,
        string marketCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var code = couponCode.Trim().ToUpperInvariant();
        var coupon = await db.Coupons.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == code && c.DeletedAt == null, cancellationToken);
        if (coupon is null || !coupon.IsActive)
        {
            return CalculateHandlerResult.Fail(404, "pricing.coupon.invalid", "Unknown or deactivated coupon.");
        }

        // Atomic overall-limit gate: UPDATE … WHERE used_count < overall_limit RETURNING 1.
        // If no row returns, limit is reached. Eliminates the read-modify-write TOCTOU on UsedCount.
        var whereClause = coupon.OverallLimit is int overall
            ? $" AND (\"OverallLimit\" IS NULL OR \"UsedCount\" < {overall})"
            : string.Empty;
        var updatedRows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE pricing.coupons
            SET "UsedCount" = "UsedCount" + 1, "UpdatedAt" = {nowUtc}
            WHERE "Id" = {coupon.Id}
              AND ("OverallLimit" IS NULL OR "UsedCount" < "OverallLimit")
            """, cancellationToken);
        _ = whereClause;
        if (updatedRows == 0)
        {
            return CalculateHandlerResult.Fail(409, "pricing.coupon.limit_reached", "Overall redemption limit reached.");
        }

        // Per-customer limit is enforced by the pair of partial unique indexes on
        // (CouponId, AccountId, [OrderId]); the insert below will trip 23505 for duplicates.
        // The explicit count is still a fast-fail heuristic that prevents a doomed insert.
        if (coupon.PerCustomerLimit is int perCust)
        {
            var usedByAccount = await db.CouponRedemptions
                .AsNoTracking()
                .CountAsync(r => r.CouponId == coupon.Id && r.AccountId == accountId, cancellationToken);
            if (usedByAccount >= perCust)
            {
                // Roll back the used_count bump we just made — otherwise it drifts.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE pricing.coupons SET "UsedCount" = "UsedCount" - 1, "UpdatedAt" = {nowUtc}
                    WHERE "Id" = {coupon.Id}
                    """, cancellationToken);
                return CalculateHandlerResult.Fail(409, "pricing.coupon.limit_reached", "Per-customer redemption limit reached.");
            }
        }

        db.CouponRedemptions.Add(new CouponRedemption
        {
            Id = Guid.NewGuid(),
            CouponId = coupon.Id,
            AccountId = accountId,
            OrderId = orderId,
            MarketCode = marketCode,
            RedeemedAt = nowUtc,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost the race. Roll back the used_count bump.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE pricing.coupons SET "UsedCount" = "UsedCount" - 1, "UpdatedAt" = {nowUtc}
                WHERE "Id" = {coupon.Id}
                """, cancellationToken);
            return CalculateHandlerResult.Fail(409, "pricing.coupon.limit_reached", "Duplicate redemption blocked.");
        }

        return CalculateHandlerResult.Success(null!);
    }
}

public sealed record CalculateHandlerResult(
    bool IsSuccess,
    CalculateResponse? Response,
    int StatusCode,
    string? ReasonCode,
    string? Detail)
{
    public static CalculateHandlerResult Success(CalculateResponse? response) =>
        new(true, response, 200, null, null);
    public static CalculateHandlerResult Fail(int status, string reason, string detail) =>
        new(false, null, status, reason, detail);
}
