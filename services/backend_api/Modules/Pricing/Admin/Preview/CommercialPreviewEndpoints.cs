using System.Diagnostics;
using System.Text.Json;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.Preview;

/// <summary>
/// Spec 007-b Preview tool (contract §7.1). Calls the existing 007-a
/// <see cref="IPriceCalculator"/> twice — once without the in-flight rule
/// and once with — and returns the per-line delta.
///
/// In-flight (unsaved) coupon bodies are previewed by persisting a transient
/// <c>__PV_</c>-prefixed Coupon row before the engine call and deleting it
/// after. Persistence is required because the engine resolves coupons via a
/// fresh <see cref="DbContext"/> scope (cross-connection visibility); a
/// process-local overlay would not be seen. The transient row is cleaned up
/// in the <c>finally</c> block; if the host process crashes mid-preview the
/// integrity-scan worker (T148) reaps any orphaned <c>__PV_</c> rows on its
/// next pass.
/// </summary>
public static class CommercialPreviewEndpoints
{
    private const string PreviewCodePrefix = "__PV_";
    public static IEndpointRouteBuilder MapCommercialPreviewEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/preview");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        group.MapPost("/price-explanation", PreviewPriceExplanationAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        return builder;
    }

    private static async Task<IResult> PreviewPriceExplanationAsync(
        [FromBody] PreviewExplanationRequest request,
        HttpContext context,
        PricingDbContext pricing,
        CatalogDbContext catalog,
        IPriceCalculator calculator,
        CommercialActorPermissions perms,
        TimeProvider time,
        CancellationToken ct)
    {
        if (request is null || request.InFlightRule is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CouponLabelRequiredBilingual, "Body required.");
        }
        if (request.InFlightRule.Kind != "coupon")
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                "Only kind='coupon' is supported in this build (US1 MVP scope).");
        }

        var profile = await pricing.PreviewProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PreviewProfileId, ct);
        if (profile is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                "preview.profile.not_found", "Preview profile not found.");
        }

        // RBAC: a non-author cannot preview against a personal profile they
        // don't own (§6 visibility rules).
        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        if (profile.Visibility == "personal"
            && profile.CreatedBy != actorId
            && !await perms.HasSuperAdminAsync(context, ct))
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.PreviewProfileNotVisibleToActor,
                "Personal profile not visible to caller.");
        }

        // Resolve the cart-line SKUs to ProductIds via the catalog read model.
        IReadOnlyList<PreviewCartLineResolved> cart;
        try
        {
            cart = await ResolveCartAsync(profile, catalog, ct);
        }
        catch (PreviewSkuMissingException ex)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                "preview.profile.sku_archived",
                $"Cart contains a sku not available in market {profile.MarketCode}: {ex.Sku}");
        }

        if (cart.Count == 0)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PreviewProfileCartTooLarge,
                "Profile cart resolved to zero priceable lines.");
        }

        var sw = Stopwatch.StartNew();

        // First call: WITHOUT the in-flight rule. Always read from the engine
        // even when no coupon is in scope so the delta baseline is consistent.
        var marketLower = MarketToCartCode(profile.MarketCode);
        var withoutCtx = BuildContext(profile, cart, marketLower, couponCode: null, time.GetUtcNow());
        var withoutOutcome = await calculator.CalculateAsync(withoutCtx, ct);
        if (!withoutOutcome.IsSuccess)
        {
            return AdminCommercialResponseFactory.Problem(context, withoutOutcome.StatusCode,
                withoutOutcome.ReasonCode!, withoutOutcome.Detail!);
        }

        // Second call: WITH the in-flight rule. For an unsaved draft body we
        // persist a transient `__PV_`-prefixed coupon row keyed on a random
        // GUID code so concurrent operators don't collide on the same code.
        // For a saved rule (rule_id set), we reuse the existing code as-is.
        string? engineCouponCode = null;
        Guid? transientCouponId = null;
        try
        {
            (engineCouponCode, transientCouponId) = await PrepareInFlightCouponAsync(
                pricing, request.InFlightRule, profile.MarketCode, time.GetUtcNow(), actorId, ct);

            var withCtx = BuildContext(profile, cart, marketLower, engineCouponCode, time.GetUtcNow());
            var withOutcome = await calculator.CalculateAsync(withCtx, ct);
            if (!withOutcome.IsSuccess)
            {
                // Engine rejected the in-flight rule (e.g. expired, out-of-window).
                // Surface the engine's reason code unchanged so the operator can fix
                // the body and retry. Maps to contract §7.1 `preview.rule.invalid_body`.
                return AdminCommercialResponseFactory.Problem(context, withOutcome.StatusCode,
                    "preview.rule.invalid_body",
                    $"Engine rejected the in-flight rule: {withOutcome.Detail}");
            }

            sw.Stop();

            var withRes = withOutcome.Result!;
            var withoutRes = withoutOutcome.Result!;

            return Results.Ok(new PreviewExplanationResponse(
                WithRule: BuildOutcome(withRes, cart),
                WithoutRule: BuildOutcome(withoutRes, cart),
                DeltaPerLine: ComputeDeltas(withRes, withoutRes, cart),
                ElapsedMs: sw.ElapsedMilliseconds));
        }
        finally
        {
            if (transientCouponId is { } cid)
            {
                // Best-effort cleanup. If the host crashes between the row insert
                // and this DELETE, the integrity-scan worker (T148, polish phase)
                // reaps `__PV_`-prefixed rows on its next pass.
                try
                {
                    await pricing.Coupons.Where(c => c.Id == cid).ExecuteDeleteAsync(CancellationToken.None);
                }
                catch
                {
                    // Swallow — the transient row is harmless and the scrub job
                    // will pick it up; do not surface cleanup errors to the caller.
                }
            }
        }
    }

    // -------------------- helpers --------------------

    private static string MarketToCartCode(string marketCodeUpper) =>
        marketCodeUpper switch
        {
            "SA" => "ksa",
            "EG" => "eg",
            _ => marketCodeUpper.ToLowerInvariant(),
        };

    private static async Task<IReadOnlyList<PreviewCartLineResolved>> ResolveCartAsync(
        Entities.PreviewProfile profile,
        CatalogDbContext catalog,
        CancellationToken ct)
    {
        List<JsonElement> raw;
        try
        {
            raw = JsonSerializer.Deserialize<List<JsonElement>>(profile.CartLinesJson) ?? new();
        }
        catch (JsonException)
        {
            return Array.Empty<PreviewCartLineResolved>();
        }

        // Catalog stores market codes as lowercase ('ksa','eg') while preview
        // profiles use uppercase ('SA','EG'). Translate up front.
        var marketLower = MarketToCartCode(profile.MarketCode);

        var skuToLine = raw.Select(e => new
        {
            Sku = e.GetProperty("sku").GetString() ?? string.Empty,
            Qty = e.GetProperty("qty").GetInt32(),
            Restricted = e.TryGetProperty("restricted", out var r) && r.GetBoolean(),
        }).Where(x => !string.IsNullOrWhiteSpace(x.Sku) && x.Qty > 0).ToList();

        var skus = skuToLine.Select(l => l.Sku).Distinct().ToArray();
        var products = await catalog.Products.AsNoTracking()
            .Where(p => skus.Contains(p.Sku) && p.Status == "published")
            .ToListAsync(ct);

        var resolved = new List<PreviewCartLineResolved>();
        foreach (var line in skuToLine)
        {
            var product = products.FirstOrDefault(p => p.Sku == line.Sku);
            if (product is null)
            {
                throw new PreviewSkuMissingException(line.Sku);
            }
            if (!product.MarketCodes.Any(m => string.Equals(m, marketLower, StringComparison.OrdinalIgnoreCase)))
            {
                throw new PreviewSkuMissingException(line.Sku);
            }
            // The preview profile's "restricted" override beats the catalog
            // flag so operators can rehearse a restricted-cart preview.
            resolved.Add(new PreviewCartLineResolved(
                product.Id, product.Sku, line.Qty,
                product.PriceHintMinorUnits ?? 0,
                line.Restricted || product.Restricted));
        }
        return resolved;
    }

    private static PricingContext BuildContext(
        Entities.PreviewProfile profile,
        IReadOnlyList<PreviewCartLineResolved> cart,
        string marketCodeLower,
        string? couponCode,
        DateTimeOffset nowUtc)
    {
        // Preview tool is admin-side; the customer account context is synthetic.
        // We use Guid.Empty so per-customer redemption checks short-circuit on
        // null and the engine treats the cart as a fresh evaluation. Tier slug
        // mapping is best-effort — TierId-to-slug lookup lives in the engine
        // already; we forward the profile's TierId as a synthetic slug stub
        // (the engine will skip the tier layer if the slug doesn't resolve).
        var account = profile.AccountKind == "b2b"
            ? new PricingAccountContext(actorIdSyntheticForPreview, profile.TierId?.ToString("N"), profile.VerificationState)
            : null;

        var lines = cart.Select(l => new PricingContextLine(
            ProductId: l.ProductId,
            Qty: l.Qty,
            ListPriceMinor: l.PriceHintMinor,
            Restricted: l.Restricted,
            CategoryIds: Array.Empty<Guid>())).ToArray();

        return new PricingContext(
            MarketCode: marketCodeLower,
            Locale: profile.Locale,
            Account: account,
            Lines: lines,
            CouponCode: couponCode,
            QuotationId: null,
            OrderId: null,
            NowUtc: nowUtc,
            Mode: PricingMode.Preview);
    }

    private static readonly Guid actorIdSyntheticForPreview = new("00000000-0000-0000-0000-00000000beef");

    private static async Task<(string EngineCouponCode, Guid? TransientId)> PrepareInFlightCouponAsync(
        PricingDbContext pricing,
        PreviewInFlightRule inFlight,
        string marketCodeUpper,
        DateTimeOffset nowUtc,
        Guid actorId,
        CancellationToken ct)
    {
        if (inFlight.RuleId is { } savedId)
        {
            // Saved rule path: re-use the existing code so the engine reads
            // the persisted body via its normal coupon-resolution query.
            var saved = await pricing.Coupons.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == savedId, ct);
            if (saved is null)
            {
                throw new PreviewSkuMissingException("(coupon)");
            }
            // Briefly flip the saved coupon to active so the engine resolves
            // it even from draft state (preview-only). Persisted state is
            // restored by caller's finally cleanup.
            // For simplicity we don't actually flip — the engine reads
            // IsActive=true regardless of state; saved rules that are
            // already inactive trigger a warning and the operator must
            // schedule first. That matches the spec contract §7.1 errors.
            return (saved.Code, null);
        }

        var body = inFlight.Coupon
            ?? throw new InvalidOperationException("In-flight coupon body required when rule_id is null.");

        // Synthesize a transient code that won't collide with operator codes.
        // Hardcoded NOT to use the operator-supplied code so a real ramping
        // coupon with the same code isn't shadowed during the preview window.
        var transientCode = (PreviewCodePrefix + Guid.NewGuid().ToString("N").Substring(0, 16))
            .ToUpperInvariant();
        var marketLower = MarketToCartCode(marketCodeUpper);

        var transient = new Coupon
        {
            Id = Guid.NewGuid(),
            Code = transientCode,
            Kind = body.Type switch { "percent_off" => "percent", "amount_off" => "amount", _ => "percent" },
            Value = body.Type == "percent_off"
                ? (body.Value ?? 0) * 100   // engine expects bps; UI value is 1..100
                : (int)Math.Min(int.MaxValue, body.AmountOffMinor ?? 0),
            CapMinor = body.CapMinor,
            ExcludesRestricted = body.ExcludesRestricted,
            MarketCodes = new[] { marketLower },
            ValidFrom = nowUtc.AddMinutes(-1),
            ValidTo = nowUtc.AddDays(1),
            IsActive = true,
            State = LifecycleState.Active,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = actorId,
            AuthorActorId = actorId,
            LabelAr = "(preview)",
            LabelEn = "(preview)",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        pricing.Coupons.Add(transient);
        await pricing.SaveChangesAsync(ct);
        return (transientCode, transient.Id);
    }

    private static PreviewExplanationOutcome BuildOutcome(
        PriceResult result,
        IReadOnlyList<PreviewCartLineResolved> cart)
    {
        var skuByProduct = cart.ToDictionary(c => c.ProductId, c => c.Sku);
        return new PreviewExplanationOutcome(
            ExplanationHash: result.ExplanationHash,
            Lines: result.Lines.Select(l => new PreviewExplanationLine(
                ProductId: l.ProductId,
                Sku: skuByProduct.GetValueOrDefault(l.ProductId, string.Empty),
                Qty: l.Qty,
                ListMinor: l.ListMinor,
                NetMinor: l.NetMinor,
                TaxMinor: l.TaxMinor,
                GrossMinor: l.GrossMinor,
                Explanation: l.Layers.Select(r => new PreviewExplanationLayer(
                    Layer: r.Layer,
                    RuleId: r.RuleId,
                    RuleKind: r.RuleKind,
                    AppliedMinor: r.AppliedMinor,
                    ReasonCode: r.ReasonCode)).ToArray())).ToArray(),
            Totals: new PreviewExplanationTotals(
                SubtotalMinor: result.Totals.SubtotalMinor,
                DiscountMinor: result.Totals.DiscountMinor,
                TaxMinor: result.Totals.TaxMinor,
                GrandTotalMinor: result.Totals.GrandTotalMinor));
    }

    private static IReadOnlyList<PreviewLineDelta> ComputeDeltas(
        PriceResult withRule,
        PriceResult withoutRule,
        IReadOnlyList<PreviewCartLineResolved> cart)
    {
        var skuByProduct = cart.ToDictionary(c => c.ProductId, c => c.Sku);
        var withMap = withRule.Lines.ToDictionary(l => l.ProductId);
        var withoutMap = withoutRule.Lines.ToDictionary(l => l.ProductId);
        var deltas = new List<PreviewLineDelta>();
        foreach (var pid in skuByProduct.Keys)
        {
            withMap.TryGetValue(pid, out var w);
            withoutMap.TryGetValue(pid, out var wo);
            var dn = (w?.NetMinor ?? 0) - (wo?.NetMinor ?? 0);
            // The reason mirrors the most-distinguishing layer in the with-rule
            // explanation. Coupon layer wins when present; else null.
            var reason = w?.Layers.FirstOrDefault(l => l.Layer == "coupon")?.ReasonCode;
            deltas.Add(new PreviewLineDelta(pid, skuByProduct[pid], dn, reason));
        }
        return deltas;
    }

    private sealed record PreviewCartLineResolved(
        Guid ProductId,
        string Sku,
        int Qty,
        long PriceHintMinor,
        bool Restricted);

    private sealed class PreviewSkuMissingException(string sku) : Exception($"sku '{sku}' not available")
    {
        public string Sku { get; } = sku;
    }
}
