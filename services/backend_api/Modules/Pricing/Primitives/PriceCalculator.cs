using System.Diagnostics;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Caches;
using BackendApi.Modules.Pricing.Primitives.Explanation;
using BackendApi.Modules.Pricing.Primitives.Layers;
using BackendApi.Modules.Pricing.Primitives.Rounding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Pricing.Primitives;

public sealed class PriceCalculator(
    IServiceScopeFactory scopeFactory,
    TaxRateCache taxRateCache,
    PromotionCache promotionCache,
    ILogger<PriceCalculator> logger) : IPriceCalculator
{
    public async Task<PriceCalculationOutcome> CalculateAsync(PricingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var marketCode = context.MarketCode.Trim().ToLowerInvariant();
        var currency = PricingConstants.ResolveCurrency(marketCode);

        // Quotation reuse: if the caller provides a quotationId and an immutable explanation exists, return verbatim.
        if (context.QuotationId is Guid qid)
        {
            await using var qscope = scopeFactory.CreateAsyncScope();
            var qdb = qscope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var existing = await qdb.PriceExplanations
                .AsNoTracking()
                .SingleOrDefaultAsync(e => e.OwnerKind == "quote" && e.OwnerId == qid, cancellationToken);

            if (existing is not null)
            {
                var replay = ReplayStoredExplanation(existing, currency);
                return PriceCalculationOutcome.Success(replay);
            }
        }

        // Build working set
        var workingLines = context.Lines
            .Select(l => new WorkingLine(l.ProductId, l.Qty, l.ListPriceMinor, l.Restricted, l.CategoryIds))
            .ToList();
        var ws = new PricingWorkingSet(context, workingLines);

        // Resolve tax rate
        var taxRate = await taxRateCache.GetAsync(marketCode, "vat", context.NowUtc, cancellationToken);
        if (taxRate is null)
        {
            return PriceCalculationOutcome.Fail(500, "pricing.tax_rate_missing", "No active VAT rate for this market.");
        }
        ws.TaxRate = taxRate;

        // Resolve tier prices
        IReadOnlyDictionary<Guid, long> tierPrices = new Dictionary<Guid, long>();
        if (context.Account is { TierSlug: { } tierSlug } account)
        {
            await using var tscope = scopeFactory.CreateAsyncScope();
            var tdb = tscope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var productIds = context.Lines.Select(l => l.ProductId).Distinct().ToArray();

            var tier = await tdb.B2BTiers
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.Slug == tierSlug && t.IsActive, cancellationToken);

            if (tier is not null)
            {
                tierPrices = await tdb.ProductTierPrices
                    .AsNoTracking()
                    .Where(tp => tp.TierId == tier.Id
                        && tp.MarketCode == marketCode
                        && productIds.Contains(tp.ProductId))
                    .ToDictionaryAsync(tp => tp.ProductId, tp => tp.NetMinor, cancellationToken);
            }
            _ = account;
        }

        // Resolve coupon (if any)
        AppliedCouponInfo? appliedCoupon = null;
        if (!string.IsNullOrWhiteSpace(context.CouponCode))
        {
            var couponOutcome = await ResolveCouponAsync(context, marketCode, cancellationToken);
            if (!couponOutcome.IsSuccess)
            {
                return couponOutcome;
            }
            appliedCoupon = couponOutcome.Coupon;
        }
        ws.AppliedCoupon = appliedCoupon;

        // Resolve promotions
        var promotions = await promotionCache.GetActivePromotionsAsync(marketCode, cancellationToken);

        // Execute layers in locked order: list → tier → promotion → coupon → tax
        new ListPriceLayer().Apply(ws);
        new B2BTierLayer(tierPrices, context.Account?.TierSlug).Apply(ws);
        new PromotionLayer(promotions).Apply(ws);
        new CouponLayer().Apply(ws);
        new TaxLayer().Apply(ws);

        // Build result + rounding self-assertion
        var subtotalMinor = ws.Lines.Sum(l => l.NetMinor);
        var taxMinor = ws.Lines.Sum(l => l.TaxMinor);
        var grossMinor = ws.Lines.Sum(l => l.NetMinor + l.TaxMinor);
        var listMinor = ws.Lines.Sum(l => l.ListMinor * l.Qty);
        var discountMinor = Math.Max(0, listMinor - subtotalMinor);

        if (subtotalMinor + taxMinor != grossMinor)
        {
            logger.LogError(
                "pricing.internal.rounding_drift subtotal={Sub} tax={Tax} gross={Gross} marketCode={Market}",
                subtotalMinor, taxMinor, grossMinor, marketCode);
            throw new InvalidOperationException("pricing.internal.rounding_drift");
        }

        var resultLines = ws.Lines.Select(l => new PriceResultLine(
            ProductId: l.ProductId,
            Qty: l.Qty,
            ListMinor: l.ListMinor,
            NetMinor: l.NetMinor,
            TaxMinor: l.TaxMinor,
            GrossMinor: l.NetMinor + l.TaxMinor,
            Layers: l.Explanation.ToArray())).ToArray();

        var totals = new PriceResultTotals(
            SubtotalMinor: subtotalMinor,
            DiscountMinor: discountMinor,
            TaxMinor: taxMinor,
            GrandTotalMinor: grossMinor);

        // Canonical JSON + hash (for determinism / refund verification)
        var canonicalPayload = new
        {
            version = 1,
            market = marketCode,
            currency,
            nowUtc = context.NowUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            lines = resultLines.Select(l => new
            {
                productId = l.ProductId,
                qty = l.Qty,
                listMinor = l.ListMinor,
                layers = l.Layers.Select(ll => new
                {
                    layer = ll.Layer,
                    ruleId = ll.RuleId,
                    ruleKind = ll.RuleKind,
                    appliedMinor = ll.AppliedMinor,
                    reasonCode = ll.ReasonCode,
                }).ToArray(),
                netMinor = l.NetMinor,
                taxMinor = l.TaxMinor,
                grossMinor = l.GrossMinor,
            }).ToArray(),
            totals = new
            {
                subtotalMinor = totals.SubtotalMinor,
                discountMinor = totals.DiscountMinor,
                taxMinor = totals.TaxMinor,
                grandTotalMinor = totals.GrandTotalMinor,
            },
        };
        var (hashString, hashBytes, canonicalBytes) = ExplanationHasher.Hash(canonicalPayload);

        Guid? explanationId = null;

        // Issue mode persists the explanation — but only when a concrete owner is provided.
        // Calling Issue with neither quotationId nor orderId is a caller bug: we'd create
        // orphaned preview rows with random IDs that nothing can resolve later.
        if (context.Mode == PricingMode.Issue)
        {
            if (context.QuotationId is null && context.OrderId is null)
            {
                return PriceCalculationOutcome.Fail(
                    400,
                    "pricing.issue_requires_owner",
                    "Issue mode requires either quotationId or orderId.");
            }
            explanationId = await PersistExplanationAsync(context, marketCode, canonicalBytes, hashBytes, grossMinor, cancellationToken);
        }

        stopwatch.Stop();

        logger.LogInformation(
            "pricing.calculate market={Market} locale={Locale} lines={LineCount} durationMs={Ms} grandTotalMinor={Grand} explanationHash={Hash} couponCodeHash={CouponHash}",
            marketCode,
            context.Locale,
            context.Lines.Count,
            stopwatch.ElapsedMilliseconds,
            grossMinor,
            hashString,
            context.CouponCode is null ? null : HashUtils.Sha256Hex(context.CouponCode.Trim().ToUpperInvariant()));

        return PriceCalculationOutcome.Success(new PriceResult(resultLines, totals, currency, hashString, explanationId));
    }

    private async Task<CouponResolution> ResolveCouponAsync(PricingContext context, string marketCode, CancellationToken cancellationToken)
    {
        var code = context.CouponCode!.Trim().ToUpperInvariant();

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();

        var coupon = await db.Coupons
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == code && c.DeletedAt == null, cancellationToken);

        if (coupon is null)
        {
            return CouponResolution.Fail(404, "pricing.coupon.invalid", "Unknown coupon code.");
        }

        if (!coupon.IsActive)
        {
            return CouponResolution.Fail(400, "pricing.coupon.invalid", "Coupon has been deactivated.");
        }

        if (!coupon.MarketCodes.Contains(marketCode, StringComparer.OrdinalIgnoreCase))
        {
            return CouponResolution.Fail(400, "pricing.coupon.invalid", "Coupon not valid in this market.");
        }

        if (coupon.ValidFrom is { } vf && context.NowUtc < vf)
        {
            return CouponResolution.Fail(400, "pricing.coupon.invalid", "Coupon not yet active.");
        }

        if (coupon.ValidTo is { } vt && context.NowUtc >= vt)
        {
            return CouponResolution.Fail(400, "pricing.coupon.expired", "Coupon has expired.");
        }

        if (coupon.OverallLimit is int overall && coupon.UsedCount >= overall)
        {
            return CouponResolution.Fail(409, "pricing.coupon.limit_reached", "Coupon overall redemption limit reached.");
        }

        if (coupon.ExcludesRestricted && context.Lines.All(l => l.Restricted))
        {
            return CouponResolution.Fail(400, "pricing.coupon.excludes_restricted", "Coupon excludes restricted products.");
        }

        if (context.Mode == PricingMode.Issue
            && coupon.PerCustomerLimit is int perCust
            && context.Account is { AccountId: var accountId })
        {
            var usedByAccount = await db.CouponRedemptions
                .AsNoTracking()
                .CountAsync(r => r.CouponId == coupon.Id && r.AccountId == accountId, cancellationToken);

            if (usedByAccount >= perCust)
            {
                return CouponResolution.Fail(409, "pricing.coupon.limit_reached", "Per-customer redemption limit reached.");
            }
        }

        return CouponResolution.Ok(new AppliedCouponInfo(
            coupon.Id,
            coupon.Code,
            coupon.Kind,
            coupon.Value,
            coupon.CapMinor,
            coupon.ExcludesRestricted));
    }

    private async Task<Guid> PersistExplanationAsync(
        PricingContext context,
        string marketCode,
        byte[] canonicalBytes,
        byte[] hashBytes,
        long grandTotalMinor,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();

        // Issue-mode guard in caller ensures at least one of QuotationId/OrderId is set.
        var ownerKind = context.QuotationId is not null ? "quote" : "order";
        var ownerId = context.QuotationId ?? context.OrderId!.Value;

        // Idempotency: if a row exists for (ownerKind, ownerId), return it verbatim
        var existing = await db.PriceExplanations
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.OwnerKind == ownerKind && e.OwnerId == ownerId, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var entity = new PriceExplanation
        {
            Id = Guid.NewGuid(),
            OwnerKind = ownerKind,
            OwnerId = ownerId,
            AccountId = context.Account?.AccountId,
            MarketCode = marketCode,
            ExplanationJson = System.Text.Encoding.UTF8.GetString(canonicalBytes),
            ExplanationHash = hashBytes,
            GrandTotalMinor = grandTotalMinor,
            CreatedAt = context.NowUtc,
        };
        db.PriceExplanations.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Concurrent insert for the same (ownerKind, ownerId). Return the committed row.
            var winner = await db.PriceExplanations
                .AsNoTracking()
                .SingleAsync(e => e.OwnerKind == ownerKind && e.OwnerId == ownerId, cancellationToken);
            return winner.Id;
        }

        return entity.Id;
    }

    private static PriceResult ReplayStoredExplanation(PriceExplanation existing, string currency)
    {
        // Minimal replay: build an "opaque" PriceResult from the persisted explanation.
        // Downstream (invoice, refund) uses explanation_json + hash verbatim; this shape hydrates
        // totals for the immediate response.
        var hashString = Convert.ToBase64String(existing.ExplanationHash)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var doc = System.Text.Json.JsonDocument.Parse(existing.ExplanationJson);
        var root = doc.RootElement;
        var totalsEl = root.GetProperty("totals");

        var totals = new PriceResultTotals(
            SubtotalMinor: totalsEl.GetProperty("subtotalMinor").GetInt64(),
            DiscountMinor: totalsEl.GetProperty("discountMinor").GetInt64(),
            TaxMinor: totalsEl.GetProperty("taxMinor").GetInt64(),
            GrandTotalMinor: totalsEl.GetProperty("grandTotalMinor").GetInt64());

        var linesEl = root.GetProperty("lines");
        var lines = new List<PriceResultLine>(linesEl.GetArrayLength());
        foreach (var lineEl in linesEl.EnumerateArray())
        {
            var layersEl = lineEl.GetProperty("layers");
            var layers = new List<ExplanationRow>(layersEl.GetArrayLength());
            foreach (var layerEl in layersEl.EnumerateArray())
            {
                layers.Add(new ExplanationRow(
                    Layer: layerEl.GetProperty("layer").GetString() ?? "",
                    RuleId: layerEl.TryGetProperty("ruleId", out var rid) ? rid.GetString() : null,
                    RuleKind: layerEl.TryGetProperty("ruleKind", out var rk) ? rk.GetString() : null,
                    AppliedMinor: layerEl.GetProperty("appliedMinor").GetInt64(),
                    ReasonCode: layerEl.TryGetProperty("reasonCode", out var rc) ? rc.GetString() : null));
            }
            lines.Add(new PriceResultLine(
                ProductId: lineEl.GetProperty("productId").GetGuid(),
                Qty: lineEl.GetProperty("qty").GetInt32(),
                ListMinor: lineEl.GetProperty("listMinor").GetInt64(),
                NetMinor: lineEl.GetProperty("netMinor").GetInt64(),
                TaxMinor: lineEl.GetProperty("taxMinor").GetInt64(),
                GrossMinor: lineEl.GetProperty("grossMinor").GetInt64(),
                Layers: layers));
        }

        return new PriceResult(lines, totals, currency, hashString, existing.Id);
    }
}

internal sealed record CouponResolution(bool IsSuccess, AppliedCouponInfo? Coupon, int StatusCode, string? ReasonCode, string? Detail)
{
    public static CouponResolution Ok(AppliedCouponInfo coupon) => new(true, coupon, 200, null, null);
    public static CouponResolution Fail(int status, string reason, string detail) => new(false, null, status, reason, detail);

    public static implicit operator PriceCalculationOutcome(CouponResolution r) =>
        r.IsSuccess
            ? throw new InvalidOperationException("Cannot convert successful coupon resolution to outcome directly.")
            : PriceCalculationOutcome.Fail(r.StatusCode, r.ReasonCode!, r.Detail!);
}

internal static class HashUtils
{
    public static string Sha256Hex(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
